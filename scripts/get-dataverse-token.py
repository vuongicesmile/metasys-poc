"""Emit one Dataverse access token for the local .NET worker.

This deliberately reuses the authenticated Azure CLI bundled by rmit-fm-data.
stdout is machine-readable and contains only the token.
"""

from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path


if os.name == "nt":
    RMIT_REPO = Path(r"\\wsl.localhost\Ubuntu-22.04\home\tt\data-platform-project\rmit-fm-data")
else:
    RMIT_REPO = Path("/home/tt/data-platform-project/rmit-fm-data")
DATAVERSE_URL = "https://org06cbc9ec.crm5.dynamics.com"
TENANT_ID = "31983a93-6f80-4356-a4e1-a65055e8327e"

if os.name != "nt":
    raise RuntimeError("This helper must run with the Windows Python bundled by rmit-fm-data")

result = subprocess.run(
    [
        sys.executable,
        "-m",
        "azure.cli",
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

if "--probe" in sys.argv:
    sys.stdout.write(f"TOKEN_OK_{len(access_token)}")
else:
    sys.stdout.write(access_token)
