#!/usr/bin/env python3
import sys
import json


def summarize(txt):
    try:
        obj = json.loads(txt)
    except Exception:
        return f"tool_result: {len(txt)} bytes"
    data = obj.get("data", {}) or {}
    msg = obj.get("message") or obj.get("status") or ""
    # Common tool shapes
    if "sha256" in str(data):
        ln = data.get("lengthBytes") or data.get("length") or ""
        return f"len={ln}".strip()
    if "diagnostics" in data:
        diags = data["diagnostics"] or []
        w = sum(d.get("severity", "").lower() == "warning" for d in diags)
        e = sum(d.get("severity", "").lower() in ("error", "fatal")
                for d in diags)
        ok = "OK" if not e else "FAIL"
        return f"validate: {ok} (warnings={w}, errors={e})"
    if "matches" in data:
        m = data["matches"] or []
        if m:
            first = m[0]
            return f"find_in_file: {len(m)} match(es) first@{first.get('line', 0)}:{first.get('col', 0)}"
        return "find_in_file: 0 matches"
    if "lines" in data:  # console
        lines = data["lines"] or []
        lvls = {"info": 0, "warning": 0, "error": 0}
        for L in lines:
            lvls[L.get("level", "").lower()] = lvls.get(
                L.get("level", "").lower(), 0)+1
        return f"console: {len(lines)} lines (info={lvls.get('info', 0)},warn={lvls.get('warning', 0)},err={lvls.get('error', 0)})"
    # Fallback: short status
    return (msg or "tool_result")[:80]


def prune_message(msg):
    if "content" not in msg:
        return msg
    newc = []
    for c in msg["content"]:
        if c.get("type") == "tool_result" and c.get("content"):
            out = []
            for chunk in c["content"]:
                if chunk.get("type") == "text":
                    out.append(
                        {"type": "text", "text": summarize(chunk.get("text", ""))})
            newc.append({"type": "tool_result", "tool_use_id": c.get(
                "tool_use_id"), "content": out})
        else:
            newc.append(c)
    msg["content"] = newc
    return msg


def main():
    convo = json.load(sys.stdin)
    if isinstance(convo, dict) and "messages" in convo:
        convo["messages"] = [prune_message(m) for m in convo["messages"]]
    elif isinstance(convo, list):
        convo = [prune_message(m) for m in convo]
    json.dump(convo, sys.stdout, ensure_ascii=False)


main()
