﻿// <copyright file="Bot.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace WaterCoolerAPI.Bot
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.Graph;
    using Microsoft.Graph.Communications.Calls;
    using Microsoft.Graph.Communications.Client;
    using Microsoft.Graph.Communications.Common;
    using Microsoft.Graph.Communications.Common.Telemetry;
    using Microsoft.Graph.Communications.Resources;
    using WaterCoolerAPI.Authentication;
    using WaterCoolerAPI.Common;
    using WaterCoolerAPI.Data;
    using WaterCoolerAPI.Extensions;
    using WaterCoolerAPI.IncidentManagement.IncidentStatus;
    using WaterCoolerAPI.Meetings;
    using WaterCoolerAPI.Repositories.RoomData;

    /// <summary>
    /// Bot for handling incidents.
    /// </summary>
    public class Bot
    {
        /// <summary>
        /// The prompt audio name for responder notification.
        /// </summary>
        /// <remarks>
        /// message: "There is an incident occured. Press '1' to join the incident meeting. Press '0' to listen to the instruction again. ".
        /// </remarks>
        public const string NotificationPromptName = "NotificationPrompt";

        /// <summary>
        /// The prompt audio name for responder transfering.
        /// </summary>
        /// <remarks>
        /// message: "Your call will be transferred to the incident meeting. Please don't hang off. ".
        /// </remarks>
        public const string TransferingPromptName = "TransferingPrompt";

        /// <summary>
        /// The prompt audio name for bot incoming calls.
        /// </summary>
        /// <remarks>
        /// message: "You are calling an incident application. It's a sample for incoming call with audio prompt.".
        /// </remarks>
        public const string BotIncomingPromptName = "BotIncomingPrompt";

        /// <summary>
        /// The prompt audio name for bot endpoint incoming calls.
        /// </summary>
        /// <remarks>
        /// message: "You are calling an incident application endpoint. It's a sample for incoming call with audio prompt.".
        /// </remarks>
        public const string BotEndpointIncomingPromptName = "BotEndpointIncomingPrompt";
        private readonly LinkedList<string> callbackLogs = new LinkedList<string>();
        private readonly IGraphLogger graphLogger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Bot" /> class.
        /// </summary>
        /// <param name="options">The bot options.</param>
        /// <param name="graphLogger">The graph logger.</param>
        public Bot(BotOptions options, IGraphLogger graphLogger)
        {
            var instanceNotificationUri = CallAffinityMiddleware.GetWebInstanceCallbackUri(
                new Uri(options.BotBaseUrl, HttpRouteConstants.OnIncomingRequestRoute));

            this.graphLogger = graphLogger;
            var name = this.GetType().Assembly.GetName().Name;
            var builder = new CommunicationsClientBuilder(
                name,
                options.AppId,
                this.graphLogger);

            var authProvider = new AuthenticationProvider(
                name,
                options.AppId,
                options.AppSecret,
                this.graphLogger);

            builder.SetAuthenticationProvider(authProvider);
            builder.SetNotificationUrl(instanceNotificationUri);
            builder.SetServiceBaseUrl(options.PlaceCallEndpointUrl);

            this.Client = builder.Build();
            this.Client.Calls().OnIncoming += this.CallsOnIncoming;
            this.Client.Calls().OnUpdated += this.CallsOnUpdated;

            this.IncidentStatusManager = new IncidentStatusManager();

            var audioBaseUri = options.BotBaseUrl;

            this.MediaMap[TransferingPromptName] = new MediaPrompt
            {
                MediaInfo = new MediaInfo
                {
                    Uri = new Uri(audioBaseUri, "audio/responder-transfering.wav").ToString(),
                    ResourceId = Guid.NewGuid().ToString(),
                },
            };

            this.MediaMap[NotificationPromptName] = new MediaPrompt
            {
                MediaInfo = new MediaInfo
                {
                    Uri = new Uri(audioBaseUri, "audio/responder-notification.wav").ToString(),
                    ResourceId = Guid.NewGuid().ToString(),
                },
            };

            this.MediaMap[BotIncomingPromptName] = new MediaPrompt
            {
                MediaInfo = new MediaInfo
                {
                    Uri = new Uri(audioBaseUri, "audio/bot-incoming.wav").ToString(),
                    ResourceId = Guid.NewGuid().ToString(),
                },
            };

            this.MediaMap[BotEndpointIncomingPromptName] = new MediaPrompt
            {
                MediaInfo = new MediaInfo
                {
                    Uri = new Uri(audioBaseUri, "audio/bot-endpoint-incoming.wav").ToString(),
                    ResourceId = Guid.NewGuid().ToString(),
                },
            };
        }

        /// <summary>
        /// Gets the collection of call handlers.
        /// </summary>
        public ConcurrentDictionary<string, CallHandler> CallHandlers { get; } = new ConcurrentDictionary<string, CallHandler>();

        /// <summary>
        /// Gets the client.
        /// </summary>
        /// <value>
        /// The client.
        /// </value>
        public ICommunicationsClient Client { get; }

        /// <summary>
        /// Gets the incident manager.
        /// </summary>
        public IncidentStatusManager IncidentStatusManager { get; }

        /// <summary>
        /// Gets the prompts dictionary.
        /// </summary>
        public Dictionary<string, MediaPrompt> MediaMap { get; } = new Dictionary<string, MediaPrompt>();

        /// <summary>
        /// add callback log for diagnostics.
        /// </summary>
        /// <param name="message">the message.</param>
        public void AddCallbackLog(string message)
        {
            this.callbackLogs.AddFirst(message);
        }

        /// <summary>
        /// get callback logs for diagnostics.
        /// </summary>
        /// <param name="maxCount">The maximum count of log lines.</param>
        /// <returns>The log line.</returns>
        public IEnumerable<string> GetCallbackLogs(int maxCount)
        {
            return this.callbackLogs.Take(maxCount);
        }

        /// <summary>
        /// Join teams meeting.
        /// </summary>
        /// <param name="roomDataEntity">RoomDataEntity instance.</param>
        /// <returns>The task for await.</returns>
        public async Task<ICall> JoinTeamsMeetingAsync(RoomDataEntity roomDataEntity)
        {
            var selectedUserIds = roomDataEntity.SelectedPeople.ToList().Select(a => a.Id).ToList();
            var incidentRequestData = new IncidentRequestData()
            {
                Name = roomDataEntity.Name,
                Time = DateTime.UtcNow,
                ObjectIds = selectedUserIds,
                JoinUrl = roomDataEntity.MeetingUrl,
            };

            var botMeetingCall = await this.JoinCallAsync(incidentRequestData).ConfigureAwait(false);
            return botMeetingCall;
        }

        /// <summary>
        /// Makes outgoing call asynchronously.
        /// </summary>
        /// <param name="makeCallBody">The outgoing call request body.</param>
        /// <param name="scenarioId">The scenario identifier.</param>
        /// <returns>The <see cref="Task"/>.</returns>
        public async Task<ICall> MakeCallAsync(MakeCallRequestData makeCallBody, Guid scenarioId)
        {
            if (makeCallBody == null)
            {
                throw new ArgumentNullException(nameof(makeCallBody));
            }

            if (makeCallBody.TenantId == null)
            {
                throw new ArgumentNullException(nameof(makeCallBody.TenantId));
            }

            if (makeCallBody.ObjectId == null)
            {
                throw new ArgumentNullException(nameof(makeCallBody.ObjectId));
            }

            var target =
                makeCallBody.IsApplication ?
                new InvitationParticipantInfo
                {
                    Identity = new IdentitySet
                    {
                        Application = new Identity
                        {
                            Id = makeCallBody.ObjectId,
                            DisplayName = $"Responder {makeCallBody.ObjectId}",
                        },
                    },
                }
                :
                new InvitationParticipantInfo
                {
                    Identity = new IdentitySet
                    {
                        User = new Identity
                        {
                            Id = makeCallBody.ObjectId,
                        },
                    },
                };

            var mediaToPrefetch = new List<MediaInfo>();
            foreach (var m in this.MediaMap)
            {
                mediaToPrefetch.Add(m.Value.MediaInfo);
            }

            var call = new Call
            {
                Targets = new[] { target },
                MediaConfig = new ServiceHostedMediaConfig { PreFetchMedia = mediaToPrefetch },
                RequestedModalities = new List<Modality> { Modality.Audio },
                TenantId = makeCallBody.TenantId,
            };

            var statefulCall = await this.Client.Calls().AddAsync(call, scenarioId: scenarioId).ConfigureAwait(false);

            this.graphLogger.Info($"Call creation complete: {statefulCall.Id}");

            return statefulCall;
        }

        /// <summary>
        /// Joins the call asynchronously.
        /// </summary>
        /// <param name="incidentRequestData">IncidentRequestData instance.</param>
        /// <returns>The <see cref="ICall"/> that was requested to join.</returns>
        public async Task<ICall> JoinCallAsync(IncidentRequestData incidentRequestData)
        {
            var scenarioId = string.IsNullOrEmpty(incidentRequestData.ScenarioId) ? Guid.NewGuid() : new Guid(incidentRequestData.ScenarioId);

            string incidentId = Guid.NewGuid().ToString();

            var incidentStatusData = new IncidentStatusData(incidentId, incidentRequestData);

            var incident = this.IncidentStatusManager.AddIncident(incidentId, incidentStatusData);

            MeetingInfo meetingInfo;
            ChatInfo chatInfo;

            (chatInfo, meetingInfo) = JoinInfo.ParseJoinURL(incidentRequestData.JoinUrl);

            var tenantId =
                incidentRequestData.TenantId ??
                (meetingInfo as OrganizerMeetingInfo)?.Organizer.GetPrimaryIdentity()?.GetTenantId();
            var mediaToPrefetch = new List<MediaInfo>();
            foreach (var m in this.MediaMap)
            {
                mediaToPrefetch.Add(m.Value.MediaInfo);
            }

            var joinParams = new JoinMeetingParameters(chatInfo, meetingInfo, new[] { Modality.Audio })
            {
                TenantId = tenantId,
            };

            var statefulCall = await this.Client.Calls().AddAsync(joinParams, scenarioId).ConfigureAwait(false);

            this.AddCallToHandlers(statefulCall, new IncidentCallContext(IncidentCallType.BotMeeting, incidentId));

            foreach (var objectId in incidentRequestData.ObjectIds)
            {
                var makeCallRequestData =
                    new MakeCallRequestData(
                        tenantId,
                        objectId,
                        "Application".Equals(incidentRequestData.ResponderType, StringComparison.OrdinalIgnoreCase));
                var responderCall = await this.MakeCallAsync(makeCallRequestData, scenarioId).ConfigureAwait(false);
                this.AddCallToHandlers(responderCall, new IncidentCallContext(IncidentCallType.ResponderNotification, incidentId));
            }

            this.graphLogger.Info($"Join Call complete: {statefulCall.Id}");

            return statefulCall;
        }

        /// <summary>
        /// Adds participants asynchronously.
        /// </summary>
        /// <param name="callLegId">which call to add participants.</param>
        /// <param name="addParticipantBody">The add participant body.</param>
        /// <returns>The <see cref="Task"/>.</returns>
        public async Task AddParticipantAsync(string callLegId, AddParticipantRequestData addParticipantBody)
        {
            if (string.IsNullOrEmpty(callLegId))
            {
                throw new ArgumentNullException(nameof(callLegId));
            }

            if (string.IsNullOrEmpty(addParticipantBody.ObjectId))
            {
                throw new ArgumentNullException(nameof(addParticipantBody.ObjectId));
            }

            var target = new IdentitySet
            {
                User = new Identity
                {
                    Id = addParticipantBody.ObjectId,
                },
            };

            await this.Client.Calls()[callLegId].Participants
                .InviteAsync(target, addParticipantBody.ReplacesCallId)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Try to end a particular call.
        /// </summary>
        /// <param name="callLegId">
        /// The call leg id.
        /// </param>
        /// <returns>
        /// The <see cref="Task"/>.
        /// </returns>
        public async Task TryDeleteCallAsync(string callLegId)
        {
            this.CallHandlers.TryGetValue(callLegId, out CallHandler handler);

            if (handler == null)
            {
                return;
            }

            try
            {
                await handler.Call.DeleteAsync().ConfigureAwait(false);
                this.graphLogger.Info("Delete call finished.");
            }
            catch (Exception ex)
            {
                this.graphLogger.Error(ex, $"Exception happened when delete the call {callLegId}");

                // in case the call deletion is failed, force remove the call in memory.
                this.Client.Calls().TryForceRemove(callLegId, out ICall call);

                throw;
            }
        }

        /// <summary>
        /// Add call to call handlers.
        /// </summary>
        /// <param name="call">The call to be added.</param>
        /// <param name="incidentCallContext">The incident call context.</param>
        private void AddCallToHandlers(ICall call, IncidentCallContext incidentCallContext)
        {
            Validator.NotNull(incidentCallContext, nameof(incidentCallContext));

            var statusData = this.IncidentStatusManager.GetIncident(incidentCallContext.IncidentId);

            CallHandler callHandler;
            InvitationParticipantInfo callee;
            switch (incidentCallContext.CallType)
            {
                case IncidentCallType.BotMeeting:
                    // Call to meeting.
                    callHandler = new MeetingCallHandler(this, call, statusData);
                    break;
                case IncidentCallType.ResponderNotification:
                    // call to an user.
                    callee = call.Resource.Targets.First();
                    callHandler = new ResponderCallHandler(this, call, callee.Identity.User.Id, statusData);
                    break;
                default:
                    throw new NotSupportedException($"Invalid call type in incident call context: {incidentCallContext.CallType}");
            }

            this.CallHandlers[call.Id] = callHandler;
        }

        /// <summary>
        /// Incoming call handler.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="args">The <see cref="CollectionEventArgs{TEntity}"/> instance containing the event data.</param>
        private void CallsOnIncoming(ICallCollection sender, CollectionEventArgs<ICall> args)
        {
        }

        /// <summary>
        /// Updated call handler.
        /// </summary>
        /// <param name="sender">The <see cref="ICallCollection"/> sender.</param>
        /// <param name="args">The <see cref="CollectionEventArgs{ICall}"/> instance containing the event data.</param>
        private void CallsOnUpdated(ICallCollection sender, CollectionEventArgs<ICall> args)
        {
            foreach (var call in args.RemovedResources)
            {
                if (this.CallHandlers.TryRemove(call.Id, out CallHandler handler))
                {
                    handler.Dispose();
                }
            }
        }
    }
}
