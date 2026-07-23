(function () {
  const chatShell = document.getElementById("chatShell");
  const botForm = document.getElementById("botRequestForm");
  const translationForm = document.getElementById("translationRequestForm");
  const isEnglish = document.documentElement.lang === "en";
  let chatConnection = null;
  let botAnswerInFlight = false;

  function text(ru, en) {
    return isEnglish ? en : ru;
  }

  function scrollChatToBottom() {
    if (chatShell) {
      window.requestAnimationFrame(() => {
        window.scrollTo({ top: document.body.scrollHeight, behavior: "smooth" });
      });
    }
  }

  function createTyping(label, className) {
    const node = document.createElement("div");
    node.className = `message typing-message ${className || ""}`.trim();
    node.innerHTML = `<div class="message-meta">${label}</div><div class="typing-dots"><span></span><span></span><span></span></div>`;
    return node;
  }

  function escapeHtml(value) {
    return String(value || "")
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;")
      .replaceAll("'", "&#039;");
  }

  function updateKnownSnapshot(lastMessageId) {
    if (!chatShell) {
      return;
    }

    const currentCount = Number(chatShell.dataset.messageCount || "0");
    chatShell.dataset.messageCount = String(currentCount + 1);
    if (lastMessageId) {
      chatShell.dataset.lastMessageId = String(lastMessageId);
    }
  }

  function appendChatMessage(payload, className) {
    if (!chatShell || !payload?.text) {
      return;
    }

    const message = document.createElement("div");
    message.className = `message ${className || "mine"}`;
    message.innerHTML = `
      <div class="message-meta">
        <span>${escapeHtml(payload.authorName || text("Вы", "You"))}</span>
        <span class="message-meta-actions"><span>${escapeHtml(payload.createdAt || "")}</span></span>
      </div>
      <p>${escapeHtml(payload.text)}</p>`;
    chatShell.appendChild(message);
    updateKnownSnapshot(payload.lastMessageId || payload.messageId);
    scrollChatToBottom();
  }

  async function requestBotAnswer() {
    if (!chatShell || !botForm || chatShell.dataset.askBot !== "true" || botAnswerInFlight) {
      return;
    }

    botAnswerInFlight = true;
    chatShell.dataset.askBot = "false";
    const ticketId = chatShell.dataset.ticketId;
    const token = botForm.querySelector("input[name='__RequestVerificationToken']")?.value;
    const typing = createTyping(chatShell.dataset.botTyping || text("Бот печатает", "Bot is typing"), "bot");
    chatShell.appendChild(typing);
    scrollChatToBottom();

    try {
      const response = await fetch(`?handler=Bot&id=${encodeURIComponent(ticketId)}`, {
        method: "POST",
        headers: {
          "Accept": "application/json",
          "RequestVerificationToken": token || ""
        }
      });
      const payload = await response.json();
      if (payload.ok) {
        typing.remove();
        appendChatMessage(payload, "bot");
        if (payload.escalated) {
          chatShell.dataset.ticketStatus = "WaitingForOperator";
        } else {
          chatShell.dataset.ticketStatus = "BotAnswered";
        }

        if (payload.hasMedia) {
          window.location.reload();
          return;
        }
      }
    } finally {
      botAnswerInFlight = false;
      typing.remove();
    }
  }

  function setupFilePickers() {
    document.querySelectorAll(".file-picker input[type='file']").forEach((input) => {
      input.addEventListener("change", () => {
        const label = input.closest(".file-picker");
        const labelText = label?.querySelector("span");
        if (labelText) {
          labelText.textContent = input.files?.[0]?.name || text("Выберите файл", "Choose file");
        }
      });
    });
  }

  function setupAvatarCropper() {
    const input = document.querySelector("[data-avatar-crop-input]");
    const modal = document.getElementById("avatarCropModal");
    const stage = document.getElementById("avatarCropStage");
    const image = document.getElementById("avatarCropImage");
    const zoom = document.getElementById("avatarCropZoom");
    const apply = document.getElementById("avatarCropApply");
    const cancel = document.getElementById("avatarCropCancel");
    const closeButton = modal?.querySelector(".avatar-crop-close");

    if (!input || !modal || !stage || !image || !zoom || !apply) {
      return;
    }

    let objectUrl = "";
    let selectedFileName = "avatar.png";
    let baseScale = 1;
    let scale = 1;
    let offsetX = 0;
    let offsetY = 0;
    let dragging = false;
    let dragStartX = 0;
    let dragStartY = 0;
    let startOffsetX = 0;
    let startOffsetY = 0;

    const render = () => {
      image.style.width = `${image.naturalWidth * baseScale}px`;
      image.style.height = `${image.naturalHeight * baseScale}px`;
      image.style.transform = `translate(-50%, -50%) translate(${offsetX}px, ${offsetY}px) scale(${scale})`;
    };

    const resetInput = () => {
      input.value = "";
      const labelText = input.closest(".file-picker")?.querySelector("span");
      if (labelText) {
        labelText.textContent = text("Выберите файл", "Choose file");
      }
    };

    const close = (clearInput) => {
      modal.hidden = true;
      image.removeAttribute("src");
      if (objectUrl) {
        URL.revokeObjectURL(objectUrl);
        objectUrl = "";
      }
      if (clearInput) {
        resetInput();
      }
    };

    const open = (file) => {
      selectedFileName = file.name || "avatar.png";
      if (objectUrl) {
        URL.revokeObjectURL(objectUrl);
      }
      objectUrl = URL.createObjectURL(file);
      image.onload = () => {
        const rect = stage.getBoundingClientRect();
        baseScale = Math.min(rect.width / image.naturalWidth, rect.height / image.naturalHeight);
        scale = 1;
        offsetX = 0;
        offsetY = 0;
        zoom.value = "1";
        render();
      };
      image.src = objectUrl;
      modal.hidden = false;
      apply.focus();
    };

    input.addEventListener("change", () => {
      const file = input.files?.[0];
      if (!file || !file.type.startsWith("image/")) {
        return;
      }

      open(file);
    });

    zoom.addEventListener("input", () => {
      scale = Number(zoom.value || "1");
      render();
    });

    const pointerPosition = (event) => {
      const point = event.touches?.[0] || event;
      return { x: point.clientX, y: point.clientY };
    };

    stage.addEventListener("pointerdown", (event) => {
      event.preventDefault();
      dragging = true;
      stage.setPointerCapture?.(event.pointerId);
      const point = pointerPosition(event);
      dragStartX = point.x;
      dragStartY = point.y;
      startOffsetX = offsetX;
      startOffsetY = offsetY;
    });

    stage.addEventListener("pointermove", (event) => {
      if (!dragging) {
        return;
      }

      const point = pointerPosition(event);
      offsetX = startOffsetX + point.x - dragStartX;
      offsetY = startOffsetY + point.y - dragStartY;
      render();
    });

    const stopDrag = (event) => {
      dragging = false;
      if (event?.pointerId != null) {
        stage.releasePointerCapture?.(event.pointerId);
      }
    };

    stage.addEventListener("pointerup", stopDrag);
    stage.addEventListener("pointercancel", stopDrag);
    stage.addEventListener("pointerleave", stopDrag);

    const makeCroppedFile = async () => {
      const rect = stage.getBoundingClientRect();
      const cropSize = Math.min(rect.width, rect.height) * 0.72;
      const cropLeft = (rect.width - cropSize) / 2;
      const cropTop = (rect.height - cropSize) / 2;
      const displayWidth = image.naturalWidth * baseScale * scale;
      const displayHeight = image.naturalHeight * baseScale * scale;
      const imageLeft = rect.width / 2 + offsetX - displayWidth / 2;
      const imageTop = rect.height / 2 + offsetY - displayHeight / 2;
      const outputSize = 512;
      const canvas = document.createElement("canvas");
      canvas.width = outputSize;
      canvas.height = outputSize;
      const context = canvas.getContext("2d");

      context.fillStyle = "#ffffff";
      context.fillRect(0, 0, outputSize, outputSize);
      context.drawImage(
        image,
        (imageLeft - cropLeft) / cropSize * outputSize,
        (imageTop - cropTop) / cropSize * outputSize,
        displayWidth / cropSize * outputSize,
        displayHeight / cropSize * outputSize);

      return new Promise((resolve) => {
        canvas.toBlob((blob) => {
          const safeName = selectedFileName.replace(/\.[^.]+$/, "") || "avatar";
          resolve(new File([blob], `${safeName}.png`, { type: "image/png" }));
        }, "image/png", 0.95);
      });
    };

    apply.addEventListener("click", async () => {
      const cropped = await makeCroppedFile();
      const transfer = new DataTransfer();
      transfer.items.add(cropped);
      input.files = transfer.files;

      const preview = document.getElementById("avatarPreviewImage");
      const initials = document.getElementById("avatarPreviewInitials");
      const previewUrl = URL.createObjectURL(cropped);
      if (preview) {
        preview.src = previewUrl;
      } else if (initials) {
        const img = document.createElement("img");
        img.id = "avatarPreviewImage";
        img.className = initials.className;
        img.alt = "avatar";
        img.src = previewUrl;
        initials.replaceWith(img);
      }

      close(false);
      input.closest("form")?.requestSubmit();
    });

    cancel?.addEventListener("click", () => close(true));
    closeButton?.addEventListener("click", () => close(true));
    modal.addEventListener("click", (event) => {
      if (event.target === modal) {
        close(true);
      }
    });
    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape" && !modal.hidden) {
        close(true);
      }
    });
  }

  function showRemoteTyping(displayName) {
    if (!chatShell) {
      return;
    }

    let indicator = document.querySelector(".remote-typing-indicator");
    if (!indicator) {
      indicator = document.querySelector(".operator-typing");
    }
    if (!indicator) {
      indicator = document.createElement("div");
      indicator.className = "typing-indicator remote-typing-indicator";
      chatShell.insertAdjacentElement("afterend", indicator);
    }

    indicator.textContent = `${displayName || text("Собеседник", "Contact")} ${text("печатает", "is typing")}`;
    indicator.hidden = false;
    window.clearTimeout(Number(indicator.dataset.hideTimer || 0));
    indicator.dataset.hideTimer = String(window.setTimeout(() => {
      indicator.hidden = true;
    }, 1600));
  }

  function setupTypingBroadcast(connection) {
    const form = document.querySelector(".chat-form");
    const textarea = form?.querySelector("textarea");
    if (!chatShell || !form || !textarea || !connection) {
      return;
    }

    let lastSentAt = 0;
    textarea.addEventListener("input", async () => {
      const now = Date.now();
      if (now - lastSentAt < 1200 || connection.state !== signalR.HubConnectionState.Connected) {
        return;
      }

      lastSentAt = now;
      try {
        await connection.invoke("Typing", Number(chatShell.dataset.ticketId));
      } catch {
        // Индикатор печати не должен мешать набору сообщения.
      }
    });
  }

  function setupCtrlEnterSubmit() {
    document.querySelectorAll("form textarea, form input:not([type='file'])").forEach((field) => {
      field.addEventListener("keydown", (event) => {
        if ((event.ctrlKey || event.metaKey) && event.key === "Enter") {
          event.preventDefault();
          field.closest("form")?.requestSubmit();
        }
      });
    });
  }

  function setupSubmitLocks() {
    document.querySelectorAll("form").forEach((form) => {
      form.addEventListener("submit", (event) => {
        if (event.defaultPrevented || form.classList.contains("message-delete-form")) {
          return;
        }

        if (form.dataset.ajaxVideoUpload === "true") {
          return;
        }

        const validator = window.jQuery?.(form).data("validator");
        if (validator && form.classList.contains("bot-test-form")) {
          validator.settings.messages["Input.MachineId"] = {
            ...validator.settings.messages["Input.MachineId"],
            required: text("Выберите станок.", "Select a machine.")
          };
          validator.settings.messages["Input.Question"] = {
            ...validator.settings.messages["Input.Question"],
            required: text("Введите вопрос.", "Enter a question.")
          };
        }

        if (!form.checkValidity() || (validator && !window.jQuery(form).valid())) {
          event.preventDefault();
          return;
        }

        if (form.dataset.submitting === "true") {
          event.preventDefault();
          return;
        }

        form.dataset.submitting = "true";
        form.setAttribute("aria-busy", "true");
        form.querySelectorAll("button[type='submit'], input[type='submit']").forEach((button) => {
          button.disabled = true;
          button.dataset.originalText = button.textContent || "";
          if (button.tagName === "BUTTON") {
            button.textContent = text("Отправляется...", "Sending...");
          }
        });
      });
    });
  }

  function isVideoFile(file) {
    if (!file) {
      return false;
    }

    const name = (file.name || "").toLowerCase();
    return (file.type || "").startsWith("video/")
      || [".mp4", ".mov", ".avi", ".mkv", ".webm"].some((ext) => name.endsWith(ext));
  }

  function videoProgressMarkup(label, percent) {
    const safePercent = Math.max(0, Math.min(100, Number(percent || 0)));
    return `
      <div class="video-state">
        <div class="video-progress-box">
          <strong>${label}</strong>
          <div class="video-progress"><span style="width:${safePercent}%"></span></div>
          <small>${safePercent}%</small>
        </div>
      </div>`;
  }

  function appendTemporaryVideoMessage(fileName) {
    if (!chatShell) {
      return null;
    }

    const message = document.createElement("div");
    message.className = "message mine pending-video-message";
    message.innerHTML = `
      <div class="message-meta">
        <span>${text("Вы", "You")}</span>
        <span>${new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}</span>
      </div>
      <div class="attachments">
        <div class="video-card" data-video-temp="true" data-video-status="Uploading">
          ${videoProgressMarkup(text("Видео загружается", "Video is uploading"), 0)}
          <span>${fileName || ""}</span>
        </div>
      </div>`;
    chatShell.appendChild(message);
    scrollChatToBottom();
    return message.querySelector(".video-card");
  }

  function setVideoProgress(card, label, percent) {
    if (!card) {
      return;
    }

    const name = card.querySelector("span")?.textContent || "";
    card.innerHTML = `${videoProgressMarkup(label, percent)}<span>${name}</span>`;
  }

  function pollVideoStatus(attachmentId, card) {
    if (!chatShell || !attachmentId || !card) {
      return;
    }

    const ticketId = chatShell.dataset.ticketId;
    let attempts = 0;
    const timer = window.setInterval(async () => {
      attempts += 1;
      if (attempts > 240 || card.dataset.videoStatus === "Ready" || card.dataset.videoStatus === "Failed") {
        window.clearInterval(timer);
        return;
      }

      try {
        const response = await fetch(`?handler=AttachmentStatus&id=${encodeURIComponent(ticketId)}&attachmentId=${encodeURIComponent(attachmentId)}`, {
          headers: { "Accept": "application/json" },
          cache: "no-store"
        });
        const payload = await response.json();
        if (!payload.ok) {
          return;
        }

        if (payload.status === "Ready" || payload.finalPath) {
          updateVideoCard(payload, false);
          window.clearInterval(timer);
        } else if (payload.status === "Failed") {
          updateVideoCard(payload, true);
          window.clearInterval(timer);
        }
      } catch {
        // SignalR остается основным каналом обновления, polling здесь только страховка.
      }
    }, 1500);
  }

  function setupVideoAjaxUpload() {
    document.querySelectorAll("form.chat-form").forEach((form) => {
      form.addEventListener("submit", (event) => {
        const fileInput = form.querySelector("input[type='file']");
        const file = fileInput?.files?.[0];
        if (!isVideoFile(file)) {
          return;
        }

        event.preventDefault();
        event.stopImmediatePropagation();
        if (form.dataset.submitting === "true") {
          return;
        }

        form.dataset.submitting = "true";
        form.dataset.ajaxVideoUpload = "true";
        form.setAttribute("aria-busy", "true");
        const submitButton = form.querySelector("button[type='submit']");
        if (submitButton) {
          submitButton.disabled = true;
        }

        const card = appendTemporaryVideoMessage(file.name);
        const xhr = new XMLHttpRequest();
        xhr.open("POST", form.action || window.location.href);
        xhr.setRequestHeader("X-Requested-With", "XMLHttpRequest");

        xhr.upload.onprogress = (progressEvent) => {
          if (!progressEvent.lengthComputable) {
            setVideoProgress(card, text("Видео загружается", "Video is uploading"), 0);
            return;
          }

          const percent = Math.round((progressEvent.loaded / progressEvent.total) * 100);
          setVideoProgress(card, text("Видео загружается", "Video is uploading"), percent);
        };

        xhr.onload = () => {
          let payload = {};
          try {
            payload = JSON.parse(xhr.responseText || "{}");
            if (!payload.ok || !payload.attachment?.id) {
              throw new Error(payload.error || `HTTP ${xhr.status}`);
            }

            card.dataset.videoAttachmentId = payload.attachment.id;
            card.dataset.videoStatus = payload.attachment.status || "Processing";
            card.removeAttribute("data-video-temp");
            setVideoProgress(card, text("Видео преобразовывается", "Video is converting"), 5);
            pollVideoStatus(payload.attachment.id, card);
            form.reset();
            const labelText = form.querySelector(".file-picker span");
            if (labelText) {
              labelText.textContent = text("Выберите файл", "Choose file");
            }
          } catch (error) {
            if (card) {
              const details = error?.message ? `: ${error.message}` : "";
              card.innerHTML = `<div class="video-state failed">${text("Видео не загружено", "Video upload failed")}${details}</div>`;
            }
          } finally {
            form.dataset.submitting = "false";
            form.dataset.ajaxVideoUpload = "false";
            form.removeAttribute("aria-busy");
            if (submitButton) {
              submitButton.disabled = false;
            }
          }
        };

        xhr.onerror = () => {
          if (card) {
            card.innerHTML = `<div class="video-state failed">${text("Видео не загружено", "Video upload failed")}: ${text("ошибка сети", "network error")}</div>`;
          }
          form.dataset.submitting = "false";
          form.dataset.ajaxVideoUpload = "false";
          form.removeAttribute("aria-busy");
          if (submitButton) {
            submitButton.disabled = false;
          }
        };

        xhr.send(new FormData(form));
      }, true);
    });
  }

  function setupChatAjaxMessages() {
    document.querySelectorAll("form.chat-form").forEach((form) => {
      form.addEventListener("submit", async (event) => {
        const fileInput = form.querySelector("input[type='file']");
        const file = fileInput?.files?.[0];
        if (file) {
          return;
        }

        event.preventDefault();
        event.stopImmediatePropagation();
        if (form.dataset.submitting === "true") {
          return;
        }

        const textarea = form.querySelector("textarea");
        if (!textarea || !textarea.value.trim()) {
          return;
        }

        form.dataset.submitting = "true";
        form.setAttribute("aria-busy", "true");
        const submitButton = form.querySelector("button[type='submit']");
        if (submitButton) {
          submitButton.disabled = true;
        }

        try {
          const response = await fetch(form.action || window.location.href, {
            method: "POST",
            body: new FormData(form),
            headers: {
              "Accept": "application/json",
              "X-Requested-With": "XMLHttpRequest"
            },
            cache: "no-store"
          });
          const payload = await response.json();
          if (payload.ok) {
            appendChatMessage(payload, "mine");
            form.reset();
            if (chatShell && botForm && !chatShell.dataset.operatorUserId && chatShell.dataset.ticketStatus !== "Closed") {
              chatShell.dataset.askBot = "true";
              requestBotAnswer();
            }
          }
        } finally {
          form.dataset.submitting = "false";
          form.removeAttribute("aria-busy");
          if (submitButton) {
            submitButton.disabled = false;
          }
        }
      }, true);
    });
  }

  function setupExistingVideoStatusPolling() {
    document.querySelectorAll("[data-video-attachment-id]").forEach((card) => {
      if (card.dataset.videoStatus === "Ready" || card.dataset.videoStatus === "Failed") {
        return;
      }

      pollVideoStatus(card.dataset.videoAttachmentId, card);
    });
  }

  function setupMessageDeleteConfirm() {
    let activeForm = null;
    let modal = document.querySelector(".delete-confirm-modal");

    if (!modal) {
      modal = document.createElement("div");
      modal.className = "delete-confirm-modal";
      modal.hidden = true;
      modal.innerHTML = `
        <div class="delete-confirm-card" role="dialog" aria-modal="true" aria-labelledby="deleteConfirmTitle">
          <div class="delete-confirm-icon">×</div>
          <div>
            <h2 id="deleteConfirmTitle">${text("Удалить сообщение?", "Delete message?")}</h2>
            <p>${text("Сообщение исчезнет у всех участников этого чата.", "The message will disappear for everyone in this chat.")}</p>
          </div>
          <div class="delete-confirm-actions">
            <button type="button" class="btn-secondary delete-confirm-cancel">${text("Отмена", "Cancel")}</button>
            <button type="button" class="btn-danger delete-confirm-submit">${text("Удалить", "Delete")}</button>
          </div>
        </div>`;
      document.body.appendChild(modal);
    }

    const close = () => {
      modal.hidden = true;
      activeForm = null;
    };

    modal.querySelector(".delete-confirm-cancel")?.addEventListener("click", close);
    modal.addEventListener("click", (event) => {
      if (event.target === modal) {
        close();
      }
    });
    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape" && !modal.hidden) {
        close();
      }
    });
    modal.querySelector(".delete-confirm-submit")?.addEventListener("click", () => {
      if (!activeForm) {
        close();
        return;
      }

      activeForm.dataset.deleteConfirmed = "true";
      modal.hidden = true;
      activeForm.requestSubmit();
    });

    document.querySelectorAll(".message-delete-form").forEach((form) => {
      form.addEventListener("submit", (event) => {
        if (form.dataset.deleteConfirmed === "true") {
          return;
        }

        event.preventDefault();
        event.stopImmediatePropagation();
        activeForm = form;
        modal.hidden = false;
        modal.querySelector(".delete-confirm-cancel")?.focus();
      });
    });
  }

  function setupChatPolling() {
    if (!chatShell) {
      return;
    }

    const ticketId = chatShell.dataset.ticketId;
    if (!ticketId) {
      return;
    }

    let isChecking = false;
    const initial = {
      status: chatShell.dataset.ticketStatus || "",
      operatorUserId: chatShell.dataset.operatorUserId || "",
      messageCount: Number(chatShell.dataset.messageCount || "0"),
      lastMessageId: Number(chatShell.dataset.lastMessageId || "0")
    };

    window.setInterval(async () => {
      if (isChecking || document.hidden) {
        return;
      }

      if (document.querySelector("form[data-submitting='true']")) {
        return;
      }

      isChecking = true;
      try {
        const response = await fetch(`?handler=Snapshot&id=${encodeURIComponent(ticketId)}`, {
          method: "GET",
          headers: {
            "Accept": "application/json"
          },
          cache: "no-store"
        });
        const snapshot = await response.json();
        if (!snapshot.ok) {
          return;
        }

        const changed = snapshot.status !== (chatShell.dataset.ticketStatus || initial.status)
          || (snapshot.operatorUserId || "") !== initial.operatorUserId
          || Number(snapshot.activeOperatorCount || 0) !== Number(chatShell.dataset.activeOperatorCount || 0)
          || Number(snapshot.messageCount || 0) !== Number(chatShell.dataset.messageCount || initial.messageCount)
          || Number(snapshot.lastMessageId || 0) !== Number(chatShell.dataset.lastMessageId || initial.lastMessageId);

        if (changed) {
          window.location.reload();
        }
      } catch {
        // Keep the current chat usable if a background refresh fails.
      } finally {
        isChecking = false;
      }
    }, 3000);
  }

  function setupOperatorTimeTracking() {
    const activityForm = document.getElementById("operatorActivityForm");
    if (!chatShell || !activityForm || chatShell.dataset.trackOperatorTime !== "true") {
      return;
    }

    const ticketId = chatShell.dataset.ticketId;
    const token = activityForm.querySelector("input[name='__RequestVerificationToken']")?.value;
    let lastUserActivity = Date.now();
    let lastSentAt = 0;
    let sending = false;
    let activeDebounce = 0;

    const markActive = () => {
      lastUserActivity = Date.now();
      window.clearTimeout(activeDebounce);
      activeDebounce = window.setTimeout(() => {
        sendActivity(true);
      }, 800);
    };

    ["mousemove", "keydown", "scroll", "touchstart", "click"].forEach((eventName) => {
      window.addEventListener(eventName, markActive, { passive: true });
    });

    async function sendActivity(force, keepalive) {
      const now = Date.now();
      const minInterval = force ? 8000 : 25000;
      if (sending || document.hidden || now - lastUserActivity > 5 * 60 * 1000 || now - lastSentAt < minInterval) {
        return;
      }

      sending = true;
      lastSentAt = now;
      try {
        await fetch(`?handler=Activity&id=${encodeURIComponent(ticketId)}`, {
          method: "POST",
          headers: {
            "RequestVerificationToken": token || ""
          },
          keepalive: keepalive === true,
          cache: "no-store"
        });
      } catch {
        // Учет времени не должен мешать оператору работать в чате.
      } finally {
        sending = false;
      }
    }

    function endActivity() {
      if (document.hidden && Date.now() - lastUserActivity > 5 * 60 * 1000) {
        return;
      }

      try {
        fetch(`?handler=EndActivity&id=${encodeURIComponent(ticketId)}`, {
          method: "POST",
          headers: {
            "RequestVerificationToken": token || ""
          },
          keepalive: true,
          cache: "no-store"
        });
      } catch {
        // Завершение сессии учета времени не должно блокировать закрытие страницы.
      }
    }

    window.setInterval(sendActivity, 30000);
    window.addEventListener("beforeunload", () => {
      endActivity();
    });
    sendActivity();
  }

  function setupImageLightbox() {
    const lightbox = document.getElementById("imageLightbox");
    const lightboxImage = lightbox?.querySelector("img");
    const closeButton = lightbox?.querySelector(".image-lightbox-close");

    if (!lightbox || !lightboxImage) {
      return;
    }

    document.querySelectorAll("[data-full-image]").forEach((button) => {
      button.addEventListener("click", () => {
        lightboxImage.src = button.dataset.fullImage || "";
        lightbox.hidden = false;
      });
    });

    const close = () => {
      lightbox.hidden = true;
      lightboxImage.removeAttribute("src");
    };

    closeButton?.addEventListener("click", close);
    lightbox.addEventListener("click", (event) => {
      if (event.target === lightbox) {
        close();
      }
    });

    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape" && !lightbox.hidden) {
        close();
      }
    });
  }

  function updateVideoCard(payload, failed) {
    const card = document.querySelector(`[data-video-attachment-id='${payload.attachmentId}']`);
    if (!card) {
      return;
    }

    card.dataset.videoStatus = payload.status || (failed ? "Failed" : "Ready");
    if (failed) {
      card.innerHTML = `<div class="video-state failed">${text("Видео не обработано", "Video failed")}: ${payload.errorMessage || ""}</div>`;
      return;
    }

    if (payload.finalPath) {
      const poster = payload.previewPath ? ` poster="${payload.previewPath}"` : "";
      card.innerHTML = `<video controls${poster}><source src="${payload.finalPath}" type="video/mp4" /></video>`;
    } else {
      card.innerHTML = `<div class="video-state">${text("Видео обрабатывается...", "Video is processing...")}</div>`;
    }
  }

  function updateVideoCard(payload, failed) {
    const card = document.querySelector(`[data-video-attachment-id='${payload.attachmentId}']`);
    if (!card) {
      return;
    }

    card.dataset.videoStatus = payload.status || (failed ? "Failed" : "Ready");
    if (failed) {
      card.innerHTML = `<div class="video-state failed">${text("Видео не обработано", "Video failed")}: ${payload.errorMessage || ""}</div>`;
      return;
    }

    if (payload.finalPath) {
      const poster = payload.previewPath ? ` poster="${payload.previewPath}"` : "";
      card.innerHTML = `<video controls${poster}><source src="${payload.finalPath}" type="video/mp4" /></video>`;
    } else {
      setVideoProgress(card, text("Видео преобразовывается", "Video is converting"), payload.percent || 10);
    }
  }

  function updateVideoProgress(payload) {
    const card = document.querySelector(`[data-video-attachment-id='${payload.attachmentId}']`);
    if (!card) {
      return;
    }

    setVideoProgress(
      card,
      payload.stage || text("Видео преобразовывается", "Video is converting"),
      payload.percent || 0);
  }

  async function setupChatSignalR() {
    if (!chatShell || !window.signalR) {
      return;
    }

    const ticketId = chatShell.dataset.ticketId;
    if (!ticketId) {
      return;
    }

    const connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/chat")
      .withAutomaticReconnect()
      .build();
    chatConnection = connection;

    connection.on("UserTyping", (payload) => showRemoteTyping(payload?.displayName));
    connection.on("VideoProcessingStarted", (payload) => updateVideoCard(payload, false));
    connection.on("VideoProcessingProgress", updateVideoProgress);
    connection.on("VideoReady", (payload) => updateVideoCard(payload, false));
    connection.on("VideoProcessingFailed", (payload) => updateVideoCard(payload, true));

    try {
      await connection.start();
      await connection.invoke("JoinTicket", Number(ticketId));
      setupTypingBroadcast(connection);
    } catch {
      // Если SignalR-клиент недоступен, существующий polling чата останется рабочим.
    }
  }

  async function setupLazyTranslations() {
    if (!translationForm) {
      return;
    }

    const token = translationForm.querySelector("input[name='__RequestVerificationToken']")?.value;
    const ticketId = chatShell?.dataset.ticketId || new URL(window.location.href).pathname.split("/").pop();
    const blocks = Array.from(document.querySelectorAll("[data-translation-message-id]"));

    for (const block of blocks) {
      const messageId = block.dataset.translationMessageId;
      const textNode = block.querySelector("p");
      try {
        const response = await fetch(`?handler=Translate&id=${encodeURIComponent(ticketId)}&messageId=${encodeURIComponent(messageId)}`, {
          method: "POST",
          headers: {
            "RequestVerificationToken": token || ""
          }
        });
        const payload = await response.json();
        if (payload.ok && payload.translation && textNode) {
          textNode.textContent = payload.translation;
          block.classList.remove("pending-translation");
        } else {
          block.remove();
        }
      } catch {
        if (textNode) {
          textNode.textContent = text("Перевод недоступен", "Translation unavailable");
        }
      }
    }
  }

  setupFilePickers();
  setupAvatarCropper();
  setupCtrlEnterSubmit();
  setupChatAjaxMessages();
  setupVideoAjaxUpload();
  setupExistingVideoStatusPolling();
  setupMessageDeleteConfirm();
  setupSubmitLocks();
  setupImageLightbox();
  setupLazyTranslations();
  setupChatPolling();
  setupOperatorTimeTracking();
  setupChatSignalR();
  scrollChatToBottom();
  requestBotAnswer();
})();

