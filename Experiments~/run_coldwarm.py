"""Measure identical cold/warm requests on a freshly started server per model.

Uses the shared --server, --model-dir, --input-dir and --output-dir options.
Server startup is excluded; the first completion is cold and the next is warm.
This protocol is explicit but is not a reconstruction of the historical 112 ms run.
"""
import run_matrix as m


def main():
    m.configure()
    rows = []
    for model, filename in m.MODELS:
        proc = m.start_server(filename)
        try:
            for phase in ("cold", "warm"):
                result = m.request("Reactive", True, m.SCENARIOS[0][1])
                rows.append({"model": model, "phase": phase, **result})
        finally:
            proc.kill()
            proc.wait()
    # Include error and content columns even if only some requests fail.
    keys = list(dict.fromkeys(k for row in rows for k in row))
    m.write_csv("coldwarm_results.csv", [{k: row.get(k, "") for k in keys} for row in rows])


if __name__ == "__main__":
    main()
