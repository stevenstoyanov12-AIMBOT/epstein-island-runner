#!/bin/bash
# Installs the asset-generation toolchain (Blender as a Python module plus mesh tools)
# when a Claude Code cloud session starts. Local sessions are left alone.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

pip install -q --disable-pip-version-check -r "$CLAUDE_PROJECT_DIR/tools/requirements.txt"
