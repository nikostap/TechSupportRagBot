"""Create stable VAPID credentials in the server's private .env file.

Run once on the server: python3 tools/configure-web-push.py /opt/classeng-support/app/.env
The script never prints the generated keys.
"""

import base64
from datetime import datetime, timezone
from pathlib import Path
import subprocess
import sys
import tempfile


def base64url(value: bytes) -> str:
    return base64.urlsafe_b64encode(value).decode("ascii").rstrip("=")


def main(env_path: Path) -> None:
    content = env_path.read_text(encoding="utf-8")
    if "Push__PublicKey=" in content or "Push__PrivateKey=" in content:
        if "Push__PublicKey=" not in content or "Push__PrivateKey=" not in content:
            raise RuntimeError("Incomplete push configuration; check the private .env file")
        print("Push credentials already exist; no changes made")
        return

    with tempfile.TemporaryDirectory() as directory:
        key_path = Path(directory) / "vapid.pem"
        subprocess.run(["openssl", "ecparam", "-name", "prime256v1", "-genkey", "-noout",
                        "-out", str(key_path)], check=True, capture_output=True)
        output = subprocess.run(["openssl", "ec", "-in", str(key_path), "-text", "-noout"],
                                check=True, capture_output=True, text=True).stdout

    values = {"priv": [], "pub": []}
    section = None
    for line in output.splitlines():
        stripped = line.strip()
        if stripped in ("priv:", "pub:"):
            section = stripped[:-1]
        elif stripped.startswith(("ASN1 OID:", "NIST CURVE:")):
            section = None
        elif section and all(char in "0123456789abcdefABCDEF:" for char in stripped):
            values[section].append(stripped.replace(":", ""))

    private_key = bytes.fromhex("".join(values["priv"]))
    public_key = bytes.fromhex("".join(values["pub"]))
    if len(private_key) != 32 or len(public_key) != 65 or public_key[0] != 4:
        raise RuntimeError("OpenSSL returned an unexpected P-256 key")

    suffix = "" if content.endswith("\n") else "\n"
    suffix += (f"Push__PublicKey={base64url(public_key)}\n"
               f"Push__PrivateKey={base64url(private_key)}\n"
               f"Push__EnabledSinceUtc={datetime.now(timezone.utc).isoformat()}\n")
    with env_path.open("a", encoding="utf-8") as stream:
        stream.write(suffix)
    print("Push credentials configured")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: configure-web-push.py path-to-private-env")
    main(Path(sys.argv[1]))
