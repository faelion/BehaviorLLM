"""Does gating the action menu fix the conditional-rule failures?

The matrix failed on two rules and an ablation showed rewording the guide does not help.
This re-runs exactly those six scenarios with the same prompt, but with the offending
action removed from the per-request schema by an availability provider, which is what
IActionAvailabilityProvider does in the package.
"""
import json, os, sys, importlib.util

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("m", os.path.join(HERE, "run_matrix.py"))
m = importlib.util.module_from_spec(spec); sys.modules["m"] = m; spec.loader.exec_module(m)

SYSTEM_DYNAMIC = open(os.path.join(HERE, "system_dynamic.txt"), encoding="utf-8-sig").read()
GATED = {
    "hurt": (json.loads(open(os.path.join(HERE, "schema_gated_hurt.json"), encoding="utf-8-sig").read()),
             ["HoldPosition", "Patrol", "Investigate", "Retreat"]),
    "quiet": (json.loads(open(os.path.join(HERE, "schema_gated_quiet.json"), encoding="utf-8-sig").read()),
              ["Patrol", "Investigate", "Chase", "Retreat"]),
}
FAILING = [s for s in m.SCENARIOS if s[0].startswith("hurt_") or s[0].startswith("quiet_")]

def ask(schema, available, state_text):
    body = {
        "messages": [{"role": "system", "content": SYSTEM_DYNAMIC},
                     {"role": "user", "content": "AVAILABLE THIS TURN: " + ", ".join(available) + "\n" + state_text}],
        "stream": False, "cache_prompt": True, "max_tokens": 48,
        "temperature": 0.2, "top_p": 0.95, "top_k": 40, "repeat_penalty": 1.0,
        "chat_template_kwargs": {"enable_thinking": False},
        "response_format": {"type": "json_schema", "json_schema": {"name": "decision", "schema": schema}},
    }
    import urllib.request, time
    req = urllib.request.Request(m.BASE + "/v1/chat/completions", data=json.dumps(body).encode("utf8"),
                                 headers={"Content-Type": "application/json"})
    t0 = time.perf_counter()
    with urllib.request.urlopen(req, timeout=180) as r:
        payload = json.loads(r.read())
    return payload["choices"][0]["message"]["content"], (time.perf_counter() - t0) * 1000

def run(model_name, model_file):
    proc = m.start_server(model_file)
    try:
        correct = 0
        for name, st, exp, args in FAILING:
            kind = "hurt" if name.startswith("hurt_") else "quiet"
            schema, available = GATED[kind]
            content, ms = ask(schema, available, st)
            d = m.parse_decision(content)
            _, _, ok = m.evaluate(d, exp, args)
            correct += ok
            print(f"    {name:20s} -> {(d or {}).get('action','?'):14s} "
                  f"{'OK ' if ok else 'BAD'}  {ms:.0f} ms", flush=True)
        print(f"  {model_name}: {correct}/{len(FAILING)} correct with the menu gated "
              f"(was 0/6 and 3/6 without gating)\n", flush=True)
    finally:
        proc.kill(); proc.wait()

if __name__ == "__main__":
    print("Same six previously-failing scenarios, Reactive + schema, menu gated per turn\n")
    for name, f in [("Qwen3.5-2B", "Qwen3.5-2B-Q4_K_M.gguf"), ("Qwen3.5-4B", "Qwen3.5-4B-Q4_K_M.gguf")]:
        print(f"  {name}:")
        run(name, f)
