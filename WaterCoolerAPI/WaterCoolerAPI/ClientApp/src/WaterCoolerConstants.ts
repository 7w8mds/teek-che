import slideOne from '../src/resources/slide1.png'
import slideTwo from '../src/resources/slide2.png'
import slideThree from '../src/resources/slide3.png'

export const constants = {
  newRoom: "Neuer Raum",
  createRoom: "Raum erstellen",
  create: "Erstellen",
  join: "Beitreten",
  charactersLeft: "Zeichen fehlen",
  roomLoadingMessage: "Geduld, dein Raum wird gestartet...",
  roomTitle: "Titel des Raumes",
  enterNewRoom: "Titel des Raumes eingeben",
  shortDescription: "Kurzbeschreibung",
  enterShortDescription: "Kurzbeschreibung eingeben",
  peoplePickerLabel: "Lade Kollegen ein",
  peoplePickerPlaceholder: "Gib einen Namen ein..",
  chooseRoomImage: "Wähle ein Logo für den Raum",
  dashboardLoadingMessage: "Bitte warten, wir laden die App",
  placeHolderImage: "https://thealmanian.com/wp-content/uploads/2019/01/product_image_thumbnail_placeholder.png",
  find: "Raum suchen",
  appTour: "Tour durch die App",
  welcomeCardContent: [{
    image: slideOne,
    title: "Trete in Kontakt mit deinen Kolleg:innen",
    summary: "Der Wasserspender ist dazu da, um Gespräche über beliebte Themen zu führen", /* The Water Cooler is all about open conversations on any topic from the weather to sport or whatever sparks your interest." */ 
  }, {
    image: slideTwo,
    title: "Erstelle deinen ersten Raum",
    summary: "Gib deinem Raum einen Namen, eine Beschreibung und wähle ein Icon." /* Next invite up to 5 colleagues and the Water Cooler Bot will call them!" */ 
  }, {
    image: slideThree,
    title: "Oder.......trete einem Raum bei",
    summary: "Klicke lediglich auf Beitreten und du wirst in den Raum springen!"
  }]
}