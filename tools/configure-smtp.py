"""Apply an operator-supplied SMTP env overlay and test delivery without printing secrets."""

import os
import hashlib
import pathlib
import shutil
import smtplib
import ssl
import sys
import tempfile
from email.message import EmailMessage


ALLOWED = {
    "Smtp__Host", "Smtp__Port", "Smtp__EnableSsl", "Smtp__From",
    "Smtp__UserName", "Smtp__Password", "Smtp__CertificateSha256",
    "Notifications__NewMessageEmailEnabled", "Notifications__NewMessageEmailSinceUtc",
}


def parse(path):
    values = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        if "=" in line and not line.startswith("#"):
            key, value = line.split("=", 1)
            values[key] = value
    return values


def apply(target, overlay):
    updates = parse(overlay)
    if set(updates) != ALLOWED:
        raise ValueError("SMTP overlay has missing or unexpected keys")
    previous = target.read_text(encoding="utf-8").splitlines()
    backup = target.with_name(".env.before-smtp-2026-10-16")
    if not backup.exists():
        shutil.copy2(target, backup)
        backup.chmod(0o600)
    output = []
    for line in previous:
        key = line.split("=", 1)[0]
        if key in updates:
            output.append(key + "=" + updates.pop(key))
        else:
            output.append(line)
    output.extend(key + "=" + value for key, value in updates.items())
    with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=target.parent, delete=False) as temp:
        os.chmod(temp.name, 0o600)
        temp.write("\n".join(output) + "\n")
        temp_name = temp.name
    os.replace(temp_name, target)


def test(target, recipient, certificate_path, no_auth=False):
    settings = parse(target)
    message = EmailMessage()
    message["From"] = settings["Smtp__From"]
    message["To"] = recipient
    message["Subject"] = "Class Engineering Support — SMTP test"
    message.set_content("SMTP test from support.classeng.com. Mail delivery is configured.")
    pem = pathlib.Path(certificate_path).read_text(encoding="ascii")
    actual = hashlib.sha256(ssl.PEM_cert_to_DER_cert(pem)).hexdigest().upper()
    expected = settings["Smtp__CertificateSha256"].replace(":", "").upper()
    if actual != expected:
        raise ValueError("SMTP certificate fingerprint mismatch")
    with smtplib.SMTP(settings["Smtp__Host"], int(settings["Smtp__Port"]), timeout=20) as client:
        client.ehlo()
        if settings["Smtp__EnableSsl"].lower() == "true":
            client.starttls(context=ssl.create_default_context(cadata=pem))
            client.ehlo()
        if not no_auth:
            client.login(settings["Smtp__UserName"], settings["Smtp__Password"])
        client.send_message(message)
    print("SMTP server accepted the test message")


if __name__ == "__main__":
    env = pathlib.Path(sys.argv[1])
    overlay = pathlib.Path(sys.argv[2])
    recipient = sys.argv[3]
    certificate_path = sys.argv[4]
    no_auth = "--no-auth" in sys.argv[5:]
    try:
        test(overlay, recipient, certificate_path, no_auth)
        if not no_auth:
            apply(env, overlay)
    except Exception as exc:
        print("SMTP setup or test failed: " + type(exc).__name__)
        raise SystemExit(1)
