#!/usr/bin/env python3
"""Собирает ai-server-setup.sh из шаблона: вкладывает в него gateway.py и chat.html (base64), чтобы скрипт работал сам по себе."""
import base64
import os

here = os.path.dirname(os.path.abspath(__file__))
def b64(name):
    with open(os.path.join(here, name), "rb") as f:
        return base64.b64encode(f.read()).decode()
t = open(os.path.join(here, "ai-server-setup.sh.in"), encoding="utf-8").read()
t = t.replace("@@GATEWAY_B64@@", b64("gateway.py")).replace("@@CHAT_B64@@", b64("chat.html"))
with open(os.path.join(here, "ai-server-setup.sh"), "w", encoding="utf-8", newline="\n") as f:
    f.write(t)
print("ai-server-setup.sh собран:", len(t), "знаков")
