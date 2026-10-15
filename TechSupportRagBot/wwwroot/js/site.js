(function () {
  const chatShell = document.getElementById("chatShell");
  const botForm = document.getElementById("botRequestForm");
  const translationForm = document.getElementById("translationRequestForm");
  const isEnglish = document.documentElement.lang === "en";
  let chatConnection = null;
  let botAnswerInFlight = false;
  let messageCursor = Number(chatShell?.dataset.lastMessageId || "0");
  let messagesLoading = null;

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

  function appendFeedMessage(payload) {
    if (!chatShell || chatShell.querySelector(`[data-message-id='${Number(payload.id)}']`)) {
      return;
    }

    const nearBottom = window.innerHeight + window.scrollY >= document.body.scrollHeight - 240;
    const message = document.createElement("div");
    message.className = `message ${["mine", "bot", "operator"].includes(payload.kind) ? payload.kind : "operator"}`;
    message.dataset.messageId = String(payload.id);
    const details = payload.authorDetails ? `<small>${escapeHtml(payload.authorDetails)}</small>` : "";
    const deleteForm = payload.canDelete && chatShell.dataset.canWrite === "true"
      ? `<form method="post" action="?handler=DeleteMessage&id=${encodeURIComponent(chatShell.dataset.ticketId)}&messageId=${Number(payload.id)}" class="message-delete-form"><button type="submit" title="${text("Удалить сообщение", "Delete message")}" aria-label="${text("Удалить сообщение", "Delete message")}">×</button></form>`
      : "";
    message.innerHTML = `<div class="message-meta"><span>${payload.kind === "bot" ? text("Бот", "Bot") : escapeHtml(payload.authorName)}${details}</span><span class="message-meta-actions"><span>${escapeHtml(payload.createdAt)}</span>${deleteForm}</span></div>`;
    if (payload.text) {
      const paragraph = document.createElement("p");
      paragraph.textContent = payload.text;
      message.appendChild(paragraph);
    }
    if (payload.needsTranslation) {
      const translation = document.createElement("div");
      translation.className = `message-translation${payload.translation ? "" : " pending-translation"}`;
      if (!payload.translation) translation.dataset.translationMessageId = String(payload.id);
      translation.innerHTML = `<span>${text("Перевод", "Translation")}</span><p>${escapeHtml(payload.translation || text("Перевод загружается...", "Loading translation..."))}</p>`;
      message.appendChild(translation);
    }
    if (payload.attachments?.length) {
      const attachments = document.createElement("div");
      attachments.className = "attachments";
      for (const item of payload.attachments) {
        const url = escapeHtml(item.url);
        const name = escapeHtml(item.fileName);
        if (item.isVideo) {
          const card = document.createElement("div");
          card.className = "video-card";
          card.dataset.videoAttachmentId = String(item.id);
          card.dataset.videoStatus = item.status;
          card.innerHTML = item.status === "Ready"
            ? `<video controls preload="metadata" poster="${escapeHtml(item.previewUrl)}"><source src="${url}" type="video/mp4" /></video><span>${name}</span>`
            : `<div class="video-state${item.status === "Failed" ? " failed" : ""}">${item.status === "Failed" ? text("Ошибка отправки", "Send failed") : text("Видео обрабатывается...", "Video is processing...")}</div><span>${name}</span>`;
          attachments.appendChild(card);
          if (item.status !== "Ready" && item.status !== "Failed") pollVideoStatus(item.id, card);
        } else if (item.isImage) {
          const button = document.createElement("button");
          button.className = "chat-image-button";
          button.type = "button";
          button.dataset.fullImage = item.url;
          button.setAttribute("aria-label", item.fileName);
          button.innerHTML = `<img src="${url}" alt="${name}" />`;
          attachments.appendChild(button);
        } else {
          attachments.insertAdjacentHTML("beforeend", `<a href="${url}" target="_blank">${name}</a>`);
        }
      }
      message.appendChild(attachments);
    }
    chatShell.appendChild(message);
    setupVideoPlayback(message);
    if (payload.needsTranslation && !payload.translation) setupLazyTranslations();
    if (nearBottom) scrollChatToBottom();
  }

  async function syncMessages() {
    if (!chatShell) return;
    if (messagesLoading) return messagesLoading;
    messagesLoading = (async () => {
      const ticketId = chatShell.dataset.ticketId;
      let batch;
      do {
        const response = await fetch(`?handler=Messages&id=${encodeURIComponent(ticketId)}&afterId=${messageCursor}`, {
          headers: { "Accept": "application/json" }, cache: "no-store"
        });
        if (!response.ok) return;
        const payload = await response.json();
        if (!payload.ok) return;
        batch = payload.messages || [];
        for (const item of batch) {
          appendFeedMessage(item);
          messageCursor = Math.max(messageCursor, Number(item.id));
        }
      } while (batch.length === 100);
      chatShell.dataset.lastMessageId = String(messageCursor);
    })();
    try { await messagesLoading; } finally { messagesLoading = null; }
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
        await syncMessages();
        if (payload.escalated) {
          chatShell.dataset.ticketStatus = "WaitingForOperator";
        } else {
          chatShell.dataset.ticketStatus = "BotAnswered";
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

  function setupPasswordToggles() {
    document.querySelectorAll("[data-password-toggle]").forEach((button) => {
      button.addEventListener("click", () => {
        const input = button.closest(".password-field")?.querySelector("input");
        if (!input) return;
        const isVisible = input.type === "text";
        input.type = isVisible ? "password" : "text";
        button.setAttribute("aria-pressed", String(!isVisible));
        button.setAttribute("aria-label", text(isVisible ? "Показать пароль" : "Скрыть пароль", isVisible ? "Show password" : "Hide password"));
        button.textContent = isVisible ? "👁" : "🙈";
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

  function setupVideoPlayback(root = document) {
    root.querySelectorAll(".video-card video").forEach((video) => {
      if (video.dataset.loadingBound === "true") {
        return;
      }
      video.dataset.loadingBound = "true";
      video.preload = "metadata";

      const frame = document.createElement("div");
      frame.className = "video-player-frame";
      video.before(frame);
      frame.appendChild(video);
      const overlay = document.createElement("div");
      overlay.className = "video-loading";
      overlay.setAttribute("role", "status");
      overlay.innerHTML = `<span class="video-spinner" aria-hidden="true"></span><span>${text("Загрузка видео...", "Loading video...")}</span>`;
      frame.appendChild(overlay);

      let loadingTimeout = null;
      const clearLoadingTimeout = () => {
        if (loadingTimeout !== null) {
          window.clearTimeout(loadingTimeout);
          loadingTimeout = null;
        }
      };
      const loading = () => {
        frame.classList.remove("is-error");
        frame.classList.add("is-loading");
        if (video.readyState < HTMLMediaElement.HAVE_METADATA) {
          clearLoadingTimeout();
          loadingTimeout = window.setTimeout(() => {
            if (video.readyState < HTMLMediaElement.HAVE_METADATA) {
              failed();
            }
          }, 12000);
        }
      };
      const ready = () => {
        clearLoadingTimeout();
        frame.classList.remove("is-loading", "is-error");
      };
      video.addEventListener("loadstart", loading);
      video.addEventListener("waiting", loading);
      video.addEventListener("seeking", loading);
      video.addEventListener("loadedmetadata", ready);
      video.addEventListener("canplay", ready);
      video.addEventListener("playing", ready);
      const failed = () => {
        clearLoadingTimeout();
        frame.classList.remove("is-loading");
        frame.classList.add("is-error");
        overlay.innerHTML = `<span>${text("Видео недоступно", "Video unavailable")}</span>`;
      };
      video.addEventListener("error", failed);
      video.querySelectorAll("source").forEach((source) => source.addEventListener("error", failed));
      if (video.networkState === HTMLMediaElement.NETWORK_NO_SOURCE) {
        failed();
      } else if (video.readyState < HTMLMediaElement.HAVE_METADATA) {
        loading();
      }
    });
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
          <span>${escapeHtml(fileName)}</span>
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
            card.closest(".message").dataset.messageId = String(payload.messageId);
            setVideoProgress(card, text("Видео преобразовывается", "Video is converting"), 5);
            pollVideoStatus(payload.attachment.id, card);
            syncMessages().catch(() => {});
            form.reset();
            const labelText = form.querySelector(".file-picker span");
            if (labelText) {
              labelText.textContent = text("Выберите файл", "Choose file");
            }
          } catch (error) {
            if (card) {
              card.innerHTML = `<div class="video-state failed">${text("Ошибка отправки", "Send failed")}</div>`;
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
            card.innerHTML = `<div class="video-state failed">${text("Ошибка отправки", "Send failed")}</div>`;
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
        if (isVideoFile(file)) {
          return;
        }

        event.preventDefault();
        event.stopImmediatePropagation();
        if (form.dataset.submitting === "true") {
          return;
        }

        const textarea = form.querySelector("textarea");
        if (!file && !textarea?.value.trim()) {
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
            await syncMessages();
            form.reset();
            const labelText = form.querySelector(".file-picker span");
            if (labelText) labelText.textContent = text("Выберите файл", "Choose file");
            if (payload.text?.trim() && chatShell && botForm && !chatShell.dataset.operatorUserId && chatShell.dataset.ticketStatus !== "Closed") {
              chatShell.dataset.askBot = "true";
              requestBotAnswer();
            }
          } else {
            window.alert(payload.error || text("Ошибка отправки", "Send failed"));
          }
        } catch {
          window.alert(text("Ошибка отправки", "Send failed"));
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
    modal.querySelector(".delete-confirm-submit")?.addEventListener("click", async () => {
      if (!activeForm) {
        close();
        return;
      }
      const form = activeForm;
      modal.hidden = true;
      activeForm = null;
      try {
        const token = form.querySelector("input[name='__RequestVerificationToken']")?.value
          || document.querySelector("input[name='__RequestVerificationToken']")?.value || "";
        const response = await fetch(form.action, {
          method: "POST",
          body: new FormData(form),
          headers: { "Accept": "application/json", "X-Requested-With": "XMLHttpRequest", "RequestVerificationToken": token }
        });
        const payload = await response.json();
        if (!payload.ok) throw new Error("Delete failed");
        form.closest(".message")?.remove();
        chatShell.dataset.messageCount = String(Math.max(0, Number(chatShell.dataset.messageCount || 0) - 1));
      } catch {
        window.alert(text("Не удалось удалить сообщение", "Could not delete message"));
      }
    });

    document.addEventListener("submit", (event) => {
      const form = event.target.closest(".message-delete-form");
      if (!form) return;
      event.preventDefault();
      activeForm = form;
      modal.hidden = false;
      modal.querySelector(".delete-confirm-cancel")?.focus();
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

        if (Number(snapshot.lastMessageId || 0) > messageCursor) await syncMessages();
        if (Number(snapshot.messageCount || 0) < chatShell.querySelectorAll("[data-message-id]").length) {
          const idsResponse = await fetch(`?handler=MessageIds&id=${encodeURIComponent(ticketId)}`, { cache: "no-store" });
          if (idsResponse.ok) {
            const idsPayload = await idsResponse.json();
            if (idsPayload.ok) {
              const ids = new Set(idsPayload.ids.map(Number));
              chatShell.querySelectorAll("[data-message-id]").forEach((node) => {
                if (!ids.has(Number(node.dataset.messageId))) node.remove();
              });
            }
          }
        }
        chatShell.dataset.ticketStatus = snapshot.status || "";
        chatShell.dataset.operatorUserId = snapshot.operatorUserId || "";
        chatShell.dataset.activeOperatorCount = String(snapshot.activeOperatorCount || 0);
        chatShell.dataset.messageCount = String(snapshot.messageCount || 0);
        if (snapshot.status === "Closed") {
          document.querySelectorAll("form.chat-form").forEach((form) => { form.hidden = true; });
        }
        if (snapshot.status) {
          const statusPill = document.querySelector(".page-head .status-pill");
          if (statusPill && snapshot.statusLabel) statusPill.textContent = snapshot.statusLabel;
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

    document.addEventListener("click", (event) => {
      const button = event.target.closest("[data-full-image]");
      if (!button) return;
      lightboxImage.src = button.dataset.fullImage || "";
      lightbox.hidden = false;
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
      card.innerHTML = `<div class="video-state failed">${text("Ошибка отправки", "Send failed")}</div>`;
      return;
    }

    if (payload.finalPath) {
      const poster = payload.previewPath ? ` poster="${payload.previewPath}"` : "";
      card.innerHTML = `<video controls preload="metadata"${poster}><source src="${payload.finalPath}" type="video/mp4" /></video>`;
      setupVideoPlayback(card);
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
      if (block.dataset.translationRequested === "true") continue;
      block.dataset.translationRequested = "true";
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
  setupPasswordToggles();
  setupAvatarCropper();
  setupCtrlEnterSubmit();
  setupChatAjaxMessages();
  setupVideoAjaxUpload();
  setupVideoPlayback();
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

