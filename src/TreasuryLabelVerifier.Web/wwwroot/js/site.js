(() => {
  "use strict";

  const input = document.getElementById("Uploads");
  const summary = document.getElementById("file-summary");
  const dropZone = document.querySelector(".drop-zone");
  const form = document.getElementById("analysis-form");
  const overlay = document.getElementById("loading-overlay");
  const label = document.querySelector(".button-label");
  const loading = document.querySelector(".button-loading");

  if (input && summary) {
    input.addEventListener("change", () => {
      const count = input.files?.length ?? 0;
      summary.textContent = count === 0
        ? "No images selected"
        : count === 1
          ? input.files[0].name
          : `${count} images selected`;
    });
  }

  if (dropZone) {
    ["dragenter", "dragover"].forEach(eventName => {
      dropZone.addEventListener(eventName, () => dropZone.classList.add("is-dragging"));
    });
    ["dragleave", "drop"].forEach(eventName => {
      dropZone.addEventListener(eventName, () => dropZone.classList.remove("is-dragging"));
    });
  }

  if (form) {
    form.addEventListener("submit", event => {
      if (window.jQuery && !window.jQuery(form).valid()) {
        return;
      }
      form.setAttribute("aria-busy", "true");
      if (overlay) overlay.hidden = false;
      if (label) label.hidden = true;
      if (loading) loading.hidden = false;
    });
  }

  if (document.querySelector(".results-section")) {
    document.querySelector(".results-section").scrollIntoView({ block: "start" });
  }
})();
