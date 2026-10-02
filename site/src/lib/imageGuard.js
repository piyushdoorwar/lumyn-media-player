// Keep site artwork from exposing browser image actions (save/drag).
// Capture-phase listeners cover images React renders later, too.
const isImage = (event) => event.target instanceof HTMLImageElement;

document.addEventListener("contextmenu", (event) => isImage(event) && event.preventDefault(), true);
document.addEventListener("dragstart", (event) => isImage(event) && event.preventDefault(), true);
