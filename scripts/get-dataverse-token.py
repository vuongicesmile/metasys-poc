"""Emit one Dataverse access token for the local .NET worker.

Use an installed Azure CLI on portable machines, or the existing bundled
azure.cli Python module on the original development machine. Never print a
credential except when the worker explicitly requests the token.
"""

from __future__ import annotations

import base64
import json
import shutil
import subprocess
import sys


DATAVERSE_URL = "https://org06cbc9ec.crm5.dynamics.com"
TENANT_ID = "31983a93-6f80-4356-a4e1-a65055e8327e"

azure_cli = shutil.which("az")
command = [azure_cli] if azure_cli else [sys.executable, "-m", "azure.cli"]

result = subprocess.run(
    command + [
        "account",
        "get-access-token",
        "--tenant",
        TENANT_ID,
        "--resource",
        DATAVERSE_URL,
        "--output",
        "json",
    ],
    check=True,
    capture_output=True,
    text=True,
    timeout=60,
)
payload = json.loads(result.stdout)
access_token = payload.get("accessToken")
if payload.get("tenant") and payload["tenant"].lower() != TENANT_ID:
    raise RuntimeError("Azure CLI returned a token for an unexpected tenant")

if not access_token or access_token.count(".") != 2:
    raise RuntimeError("Dataverse helper did not return a JWT access token")

encoded_claims = access_token.split(".")[1]
encoded_claims += "=" * (-len(encoded_claims) % 4)
claims = json.loads(base64.urlsafe_b64decode(encoded_claims))
if str(claims.get("aud", "")).rstrip("/").lower() != DATAVERSE_URL.lower():
    raise RuntimeError("Azure CLI returned a token for an unexpected resource")
if str(claims.get("tid", "")).lower() != TENANT_ID:
    raise RuntimeError("Azure CLI returned a token for an unexpected tenant")

if "--probe" in sys.argv:
    sys.stdout.write(f"TOKEN_OK_{len(access_token)}")
else:
    sys.stdout.write(access_token)
