"""BehaviorLLM experiment matrix.

Drives llama-server with the prompts and JSON Schemas produced by the package's own
PromptBuilder / ActionSchemaBuilder (generated from Unity into this folder), across
3 models x {schema on, schema off} x {Reactive, Deliberative}.

Writes matrix_results.csv (one row per configuration) and matrix_decisions.csv
(one row per decision) next to this script.
"""
import json, os, re, socket, subprocess, sys, time, urllib.request, urllib.error, statistics, csv

HERE = os.path.dirname(os.path.abspath(__file__))
SERVER = r"C:\Users\alexx\AppData\Local\Microsoft\WinGet\Packages\ggml.llamacpp_Microsoft.Winget.Source_8wekyb3d8bbwe\llama-server.exe"
MODEL_DIR = r"D:\repos\TFG\Assets\StreamingAssets\models"
PORT = 8090
BASE = f"http://127.0.0.1:{PORT}"

MODELS = [
    ("Qwen3.5-2B",   "Qwen3.5-2B-Q4_K_M.gguf"),
    ("Granite-4.1-3B", "granite-4.1-3b-Q4_K_M.gguf"),
    ("Qwen3.5-4B",   "Qwen3.5-4B-Q4_K_M.gguf"),
]

SYSTEM = {
    "Reactive":     open(os.path.join(HERE, "system_reactive.txt"), encoding="utf-8-sig").read(),
    "Deliberative": open(os.path.join(HERE, "system_deliberative.txt"), encoding="utf-8-sig").read(),
}
SCHEMA = {
    "Reactive":     json.loads(open(os.path.join(HERE, "schema_reactive.json"), encoding="utf-8-sig").read()),
    "Deliberative": json.loads(open(os.path.join(HERE, "schema_deliberative.json"), encoding="utf-8-sig").read()),
}
MAX_TOKENS = {"Reactive": 48, "Deliberative": 110}

# Valid action menu, mirroring the ActionConfig the artifacts were built from.
MENU = {
    "HoldPosition": [""],
    "Patrol":       ["Route_North", "Route_South", "Route_Perimeter"],
    "Investigate":  ["Warehouse", "Courtyard", "Gate"],
    "Chase":        ["Intruder_01", "Intruder_02"],
    "Retreat":      ["SafeZone_A", "SafeZone_B"],
}

def state(vision, memory, health):
    v = "\n".join(f"- {x}" for x in vision) if vision else "Nothing visible."
    return (f"STATE:\n--- Self-Status ---\nID: Guard_01 | Type: Self | health: {health}/100\n"
            f"--- Vision ---\n{v}\n--- Short-Term Memory ---\n- {memory}")

# (name, STATE, expected action, acceptable args or None for "any valid")
SCENARIOS = [
    ("intruder_healthy_1", state(["[4.2m] ID: Intruder_01 | Type: Hostile"], "[8.1s] Executed Action: Patrol(Route_North)", 100), "Chase", ["Intruder_01"]),
    ("intruder_healthy_2", state(["[7.5m] ID: Intruder_02 | Type: Hostile"], "[6.0s] Executed Action: Patrol(Route_South)", 95), "Chase", ["Intruder_02"]),
    ("intruder_healthy_3", state(["[2.1m] ID: Intruder_01 | Type: Hostile", "[12.0m] ID: Gate | Type: Location"], "[3.2s] Executed Action: Patrol(Route_Perimeter)", 88), "Chase", ["Intruder_01"]),
    ("intruder_healthy_4", state(["[5.5m] ID: Intruder_02 | Type: Hostile"], "[9.9s] Executed Action: HoldPosition()", 55), "Chase", ["Intruder_02"]),
    ("quiet_1", state([], "[10.4s] Executed Action: Patrol(Route_North)", 100), "Patrol", None),
    ("quiet_2", state([], "[14.7s] Executed Action: Patrol(Route_South)", 100), "Patrol", None),
    ("quiet_3", state(["[15.0m] ID: Courtyard | Type: Location"], "[5.5s] Executed Action: Patrol(Route_Perimeter)", 92), "Patrol", None),
    ("report_warehouse", state([], "[2.0s] Radio: disturbance reported at Warehouse", 100), "Investigate", ["Warehouse"]),
    ("report_courtyard", state([], "[1.5s] Radio: disturbance reported at Courtyard", 97), "Investigate", ["Courtyard"]),
    ("report_gate", state([], "[3.0s] Radio: disturbance reported at Gate", 100), "Investigate", ["Gate"]),
    ("report_hurt_no_intruder", state([], "[2.2s] Radio: disturbance reported at Warehouse", 40), "Investigate", ["Warehouse"]),
    ("report_gate_hurt", state([], "[4.0s] Radio: disturbance reported at Gate", 35), "Investigate", ["Gate"]),
    ("hurt_intruder_1", state(["[3.0m] ID: Intruder_01 | Type: Hostile"], "[7.7s] Executed Action: Chase(Intruder_01)", 30), "Retreat", None),
    ("hurt_intruder_2", state(["[6.4m] ID: Intruder_02 | Type: Hostile"], "[4.4s] Executed Action: Chase(Intruder_02)", 20), "Retreat", None),
    ("hurt_intruder_3", state(["[2.8m] ID: Intruder_01 | Type: Hostile"], "[5.0s] Executed Action: Patrol(Route_North)", 45), "Retreat", None),
    ("borderline_healthy", state(["[4.9m] ID: Intruder_01 | Type: Hostile"], "[6.6s] Executed Action: Patrol(Route_South)", 70), "Chase", ["Intruder_01"]),
]

def wait_health(proc, timeout=180):
    start = time.time()
    while time.time() - start < timeout:
        if proc.poll() is not None:
            return False
        try:
            with urllib.request.urlopen(BASE + "/health", timeout=3) as r:
                if json.loads(r.read()).get("status") == "ok":
                    return True
        except Exception:
            time.sleep(1)
    return False

def free_port():
    s = socket.socket()
    try:
        s.bind(("127.0.0.1", PORT)); return True
    except OSError:
        return False
    finally:
        s.close()

def start_server(model_file):
    args = [SERVER, "-m", os.path.join(MODEL_DIR, model_file), "--port", str(PORT),
            "--host", "127.0.0.1", "-c", "8192", "-ngl", "99", "-np", "2", "--jinja"]
    proc = subprocess.Popen(args, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if not wait_health(proc):
        proc.kill(); raise RuntimeError(f"server failed to start for {model_file}")
    return proc

def request(profile, use_schema, state_text):
    body = {
        "messages": [{"role": "system", "content": SYSTEM[profile]},
                     {"role": "user", "content": state_text}],
        "stream": False, "cache_prompt": True,
        "max_tokens": MAX_TOKENS[profile],
        "temperature": 0.2, "top_p": 0.95, "top_k": 40, "repeat_penalty": 1.0,
        "chat_template_kwargs": {"enable_thinking": False},
    }
    if use_schema:
        body["response_format"] = {"type": "json_schema",
                                   "json_schema": {"name": "decision", "schema": SCHEMA[profile]}}
    data = json.dumps(body).encode("utf8")
    req = urllib.request.Request(BASE + "/v1/chat/completions", data=data,
                                 headers={"Content-Type": "application/json"})
    t0 = time.perf_counter()
    try:
        with urllib.request.urlopen(req, timeout=180) as r:
            payload = json.loads(r.read())
    except Exception as e:
        return {"error": str(e), "latency_ms": (time.perf_counter() - t0) * 1000}
    latency = (time.perf_counter() - t0) * 1000
    msg = payload["choices"][0]["message"]
    tm = payload.get("timings") or {}
    return {"content": msg.get("content") or "", "latency_ms": latency,
            "prompt_n": tm.get("prompt_n", 0), "cache_n": tm.get("cache_n", 0),
            "predicted_n": tm.get("predicted_n", 0)}

THINK = re.compile(r"<think\b[^>]*>[\s\S]*?(</think>|$)|<\|channel>[\s\S]*?(<channel\|>|$)", re.I)

def parse_decision(text):
    """Mirrors DecisionParser: strip thinking/fences, take first balanced JSON object."""
    if not text: return None
    t = THINK.sub("", text).replace("```json", "").replace("```", "").strip()
    depth = 0; start = -1; in_str = False; esc = False
    for i, c in enumerate(t):
        if in_str:
            if esc: esc = False
            elif c == "\\": esc = True
            elif c == '"': in_str = False
            continue
        if c == '"' and depth > 0: in_str = True; continue
        if c == "{":
            if depth == 0: start = i
            depth += 1
        elif c == "}" and depth > 0:
            depth -= 1
            if depth == 0:
                try: return json.loads(t[start:i+1])
                except Exception: return None
    return None

def evaluate(d, expected_action, acceptable_args):
    if d is None or not isinstance(d, dict): return (False, False, False)
    action = (d.get("action") or "").strip()
    arg = (d.get("arg") or "").strip()
    parsed = bool(action)
    if action not in MENU: return (parsed, False, False)
    allowed = MENU[action]
    valid = (arg in allowed) if action != "HoldPosition" else (arg == "")
    if not valid: return (parsed, False, False)
    correct = action == expected_action and (acceptable_args is None or arg in acceptable_args)
    return (parsed, True, correct)

def pct(xs, p):
    if not xs: return 0.0
    s = sorted(xs); k = max(1, min(len(s), int(-(-p / 100 * len(s)) // 1)))
    return s[k - 1]

def main():
    if not free_port():
        print(f"port {PORT} is busy; stop whatever is listening and retry"); sys.exit(1)

    rows, decisions = [], []
    for model_name, model_file in MODELS:
        print(f"\n=== {model_name} : loading", flush=True)
        proc = start_server(model_file)
        try:
            for profile in ("Reactive", "Deliberative"):
                for use_schema in (True, False):
                    label = f"{profile}/{'schema' if use_schema else 'noschema'}"
                    request(profile, use_schema, SCENARIOS[0][1])  # warm the prefix, discard
                    lat, parsed_n, valid_n, correct_n = [], 0, 0, 0
                    cache_hits, prompt_tokens, completion_tokens, errors = 0, 0, 0, 0
                    for name, st, exp, args in SCENARIOS:
                        r = request(profile, use_schema, st)
                        if "error" in r:
                            errors += 1
                            decisions.append([model_name, profile, use_schema, name, "", "ERROR", 0, 0, 0, 0])
                            continue
                        d = parse_decision(r["content"])
                        p, v, c = evaluate(d, exp, args)
                        parsed_n += p; valid_n += v; correct_n += c
                        lat.append(r["latency_ms"])
                        cache_hits += r["cache_n"]
                        prompt_tokens += r["prompt_n"] + r["cache_n"]
                        completion_tokens += r["predicted_n"]
                        decisions.append([model_name, profile, use_schema, name,
                                          r["content"].replace("\n", " ")[:160],
                                          "ok" if v else "invalid", int(p), int(v), int(c),
                                          round(r["latency_ms"], 1)])
                    n = len(SCENARIOS)
                    rows.append({
                        "model": model_name, "profile": profile,
                        "structured_output": use_schema, "scenarios": n, "errors": errors,
                        "json_parse_rate": parsed_n / n, "valid_action_rate": valid_n / n,
                        "expected_action_rate": correct_n / n,
                        "mean_latency_ms": round(statistics.fmean(lat), 1) if lat else 0,
                        "p50_latency_ms": round(pct(lat, 50), 1), "p95_latency_ms": round(pct(lat, 95), 1),
                        "mean_completion_tokens": round(completion_tokens / n, 1),
                        "cache_hit_rate": round(cache_hits / prompt_tokens, 3) if prompt_tokens else 0,
                    })
                    print(f"  {label:26s} valid {valid_n}/{n}  correct {correct_n}/{n}  "
                          f"p50 {rows[-1]['p50_latency_ms']:.0f} ms", flush=True)
        finally:
            proc.kill(); proc.wait(); time.sleep(3)

    with open(os.path.join(HERE, "matrix_results.csv"), "w", newline="", encoding="utf8") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys())); w.writeheader(); w.writerows(rows)
    with open(os.path.join(HERE, "matrix_decisions.csv"), "w", newline="", encoding="utf8") as f:
        w = csv.writer(f)
        w.writerow(["model", "profile", "structured_output", "scenario", "raw_output",
                    "status", "parsed", "valid", "expected_match", "latency_ms"])
        w.writerows(decisions)
    print("\nWrote matrix_results.csv and matrix_decisions.csv")

if __name__ == "__main__":
    main()
