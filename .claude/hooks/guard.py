"""PreToolUse guard for Bash commands.

Blocks commands that would bypass onshape_kit (and with it the rate limiter, cache and
write guard) by calling the Onshape API directly, and commands that read key files.
Exit code 2 blocks the call and shows the message to Claude.
"""

import json
import re
import sys

event = json.load(sys.stdin)
command = (event.get("tool_input") or {}).get("command", "")

if re.search(r"onshape\.com/api", command, re.I):
    print("Blocked: call Onshape through the onshape MCP tools or scripts/onshape_cli.py, "
          "not directly — they enforce the rate limit and write guard.", file=sys.stderr)
    sys.exit(2)

if re.search(r"\.env(?!\.example)(\.[\w-]+)?\b", command) or re.search(r"ONSHAPE_\w*SECRET|ONSHAPE_\w*ACCESS", command):
    print("Blocked: this command touches Onshape API keys. Keys stay in the user's environment; "
          "never read or print them.", file=sys.stderr)
    sys.exit(2)

sys.exit(0)
