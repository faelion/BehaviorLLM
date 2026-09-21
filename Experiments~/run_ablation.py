"""Ablation: does the ORDER of the rules in the guide explain the failures?

The matrix failed almost only on the "retreat when hurt" rule, answering Chase.
The shipped guide states the chase rule first and unconditionally, and the retreat
exception afterwards. This re-runs the same scenarios with the exception stated
first and made explicit, changing nothing else.
"""
import json, os, sys, importlib.util

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("m", os.path.join(HERE, "run_matrix.py"))
m = importlib.util.module_from_spec(spec)
sys.modules["m"] = m
spec.loader.exec_module(m)

ORIGINAL_GUIDE = ("Chase an intruder the moment you can see one. If no intruder is visible but a disturbance was reported, "
                  "investigate it. If you are damaged below half health and an intruder is visible, retreat to a safe zone "
                  "instead of chasing. Otherwise keep patrolling your route.")

REORDERED_GUIDE = ("Check your health first. If health is below 50 and an intruder is visible, Retreat to a safe zone; never "
                   "chase while below 50 health. Otherwise, if an intruder is visible, Chase it. Otherwise, if a disturbance "
                   "was reported, Investigate that location. Otherwise Patrol a route; do not HoldPosition when nothing is "
                   "happening.")

def variant(system_text, guide):
    assert ORIGINAL_GUIDE in system_text, "guide block not found in system prompt"
    return system_text.replace(ORIGINAL_GUIDE, guide)

def run(model_name, model_file, system_text, tag):
    proc = m.start_server(model_file)
    try:
        m.SYSTEM["Reactive"] = system_text
        m.request("Reactive", True, m.SCENARIOS[0][1])  # warm
        correct = 0
        hurt_correct = 0
        hurt_total = 0
        wrong = []
        for name, st, exp, args in m.SCENARIOS:
            r = m.request("Reactive", True, st)
            d = m.parse_decision(r.get("content", ""))
            _, valid, ok = m.evaluate(d, exp, args)
            correct += ok
            if name.startswith("hurt_") or name.startswith("quiet_"):
                hurt_total += 1
                hurt_correct += ok
            if not ok:
                wrong.append(f"{name}={(d or {}).get('action','?')}")
        print(f"  {model_name:16s} {tag:12s} correct {correct}/{len(m.SCENARIOS)}  "
              f"(conditional rules {hurt_correct}/{hurt_total})", flush=True)
        if wrong:
            print(f"      still wrong: {', '.join(wrong)}", flush=True)
        return {"model": model_name, "variant": tag, "correct": correct,
                "scenarios": len(m.SCENARIOS), "conditional_correct": hurt_correct,
                "conditional_total": hurt_total, "failures": "; ".join(wrong)}
    finally:
        proc.kill(); proc.wait()

if __name__ == "__main__":
    m.configure()
    base_reactive = m.SYSTEM["Reactive"]
    original = variant(base_reactive, ORIGINAL_GUIDE)
    reordered = variant(base_reactive, REORDERED_GUIDE)

    print("Reactive + schema, same 16 scenarios, only the guide wording/order differs\n")
    rows = []
    for name, f in [("Qwen3.5-2B", "Qwen3.5-2B-Q4_K_M.gguf"), ("Qwen3.5-4B", "Qwen3.5-4B-Q4_K_M.gguf")]:
        rows.append(run(name, f, original, "original"))
        rows.append(run(name, f, reordered, "reordered"))
    m.write_csv("ablation_results.csv", rows)
