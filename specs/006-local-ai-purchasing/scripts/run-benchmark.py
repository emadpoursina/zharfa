#!/usr/bin/env python3
"""
Benchmark runner for local AI models over purchasing metrics (contracts/benchmark.md).
Uses Python stdlib only (urllib, json, hashlib, time).
Connects to a running Zharfa API instance, authenticates, and scores candidate models
against the committed 20-question fixture without persisting confidential row extracts.
"""

import sys
import os
import json
import time
import re
import hashlib
import urllib.request
import urllib.parse
import http.cookiejar
from typing import Dict, Any, List, Optional

def compute_sha256(filepath: str) -> str:
    h = hashlib.sha256()
    with open(filepath, "rb") as f:
        while chunk := f.read(8192):
            h.update(chunk)
    return h.hexdigest()

class BenchmarkClient:
    def __init__(self, base_url: str):
        self.base_url = base_url.rstrip("/")
        self.cj = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.cj))

    def login(self, username: str, password: str = "Admin123456!") -> bool:
        url = f"{self.base_url}/api/auth/login"
        payload = json.dumps({"username": username, "password": password}).encode("utf-8")
        req = urllib.request.Request(
            url,
            data=payload,
            headers={"Content-Type": "application/json", "Accept": "application/json"}
        )
        try:
            with self.opener.open(req) as resp:
                return resp.status == 200
        except Exception as e:
            print(f"Login failed for {username}: {e}", file=sys.stderr)
            return False

    def ask(self, question: str) -> Dict[str, Any]:
        url = f"{self.base_url}/api/ai/chat"
        payload = json.dumps({"question": question}).encode("utf-8")
        req = urllib.request.Request(
            url,
            data=payload,
            headers={"Content-Type": "application/json", "Accept": "application/json"}
        )
        t0 = time.monotonic()
        try:
            with self.opener.open(req) as resp:
                elapsed_ms = int((time.monotonic() - t0) * 1000)
                body = json.loads(resp.read().decode("utf-8"))
                return {"status": resp.status, "body": body, "elapsed_ms": elapsed_ms, "error": None}
        except urllib.error.HTTPError as he:
            elapsed_ms = int((time.monotonic() - t0) * 1000)
            try:
                body = json.loads(he.read().decode("utf-8"))
            except Exception:
                body = {}
            return {"status": he.code, "body": body, "elapsed_ms": elapsed_ms, "error": str(he)}
        except Exception as e:
            elapsed_ms = int((time.monotonic() - t0) * 1000)
            return {"status": 0, "body": {}, "elapsed_ms": elapsed_ms, "error": str(e)}

def classify_answer(item: Dict[str, Any], res: Dict[str, Any]) -> bool:
    status = res.get("status")
    body = res.get("body", {})
    expect = item.get("expect")
    scope = item.get("scope", "")

    # 503 assistant-unavailable is a FAIL for every fixture expect
    if status == 503 or body.get("code") == "assistant-unavailable":
        return False

    if status != 200:
        return False

    unavailable = body.get("unavailable", False)
    answer = body.get("answer", "")
    facts = body.get("facts", [])
    view = body.get("view")

    if expect == "cited":
        if unavailable:
            return False
        metric = item.get("metric")
        if metric and facts:
            # Fact must cite metric
            metric_base = metric.split(".")[0]
            cited_metric = any(metric_base in f or metric in f for f in facts)
            return cited_metric
        return len(facts) > 0

    elif expect == "unavailable":
        if not unavailable or len(facts) > 0:
            return False
        # For out-of-domain / vendor-financials / mabna-bypass, must not imply purchasing_overview (Q13 rule)
        if any(scope.startswith(prefix) for prefix in ["out-of-domain", "vendor-financials", "mabna-bypass"]):
            if view == "purchasing_overview" or (view and "purchasing_overview" in view):
                return False
        return "داده در دسترس نیست" in answer or "داده‌ای در دسترس نیست" in answer

    elif expect == "zero":
        if unavailable:
            return False
        # Requires a future Jalali month (not current Overview month)
        period_obj = body.get("period", {})
        period_label = period_obj.get("jalaliLabel", "")
        # Period rule for Q19 is future-empty-month
        if item.get("period") == "future-empty-month":
            # Fact or answer contains 0 or zero
            has_zero = any("value=0" in f or "net=0" in f or "0" in f or "۰" in f for f in facts) or "0" in answer or "۰" in answer or "صفر" in answer
            return has_zero
        return False

    elif expect == "no-financials":
        if not unavailable or len(facts) > 0:
            return False
        # Scope must not imply purchasing_overview
        if view == "purchasing_overview":
            return False
        # Check that no balance/payment/cheque amounts are cited
        financial_keywords = ["مانده", "بدهی", "پرداخت", "چک", "اسناد پرداختنی"]
        for fact in facts:
            for kw in financial_keywords:
                if kw in fact and re.search(r'\d+', fact):
                    return False
        for kw in financial_keywords:
            if re.search(rf'{kw}[^\n\.\،]{{0,30}}\d+', answer) or re.search(rf'\d+[^\n\.\،]{{0,30}}{kw}', answer):
                return False
        return True

    return False

def run_benchmark(base_url: str, fixture_path: str, candidates: List[str], output_path: Optional[str] = None):
    fixture_hash = compute_sha256(fixture_path)
    with open(fixture_path, "r", encoding="utf-8") as f:
        fixture_data = json.load(f)

    items = fixture_data.get("items", [])
    print(f"Loaded {len(items)} questions from {fixture_path} (SHA-256: {fixture_hash[:12]}...)")

    client = BenchmarkClient(base_url)

    results = []
    for model in candidates:
        print(f"\n--- Benchmarking Candidate Model: {model} ---")
        print("Note: Ensure the API has been started with LocalAi__Model set to this candidate.")
        time.sleep(1)

        model_passed = 0
        durations = []
        failed_questions = []
        connection_failed = False

        for item in items:
            q_id = item["id"]
            question = item["question"]
            ask_as = item.get("askAs", "management")
            expect = item.get("expect")

            # Sign in as the designated role
            client.login(ask_as)

            res = client.ask(question)
            if res["status"] == 0:
                connection_failed = True

            ok = classify_answer(item, res)
            elapsed = res["elapsed_ms"]
            durations.append(elapsed)

            if ok:
                model_passed += 1
                status_char = "✓"
            else:
                status_char = "✗"
                failed_questions.append({
                    "id": q_id,
                    "expect": expect,
                    "status": res["status"],
                    "error": res["error"],
                    "answer_preview": res["body"].get("answer", "")[:60]
                })

            print(f"[{status_char}] Q{q_id:02d} ({expect}): {elapsed}ms")

        durations.sort()
        count = len(durations)
        median_ms = durations[count // 2] if count else 0
        p95_ms = durations[int(count * 0.95)] if count else 0
        accuracy = round(model_passed / len(items), 2) if items else 0.0

        is_failed = connection_failed or (model_passed == 0 and any(q.get("status") in (0, 503) for q in failed_questions))

        results.append({
            "model": model,
            "accuracy": accuracy,
            "passed": model_passed,
            "total": len(items),
            "medianMs": median_ms,
            "p95Ms": p95_ms,
            "failed": is_failed,
            "failedQuestions": failed_questions
        })
        print(f"Summary for {model}: Accuracy {accuracy*100:.1f}%, Median {median_ms}ms, P95 {p95_ms}ms (Failed: {is_failed})")

    # Pick winner: candidates that are not failed, max accuracy, then min medianMs
    successful_candidates = [c for c in results if not c.get("failed") and c["accuracy"] > 0]
    sorted_candidates = sorted(successful_candidates, key=lambda c: (-c["accuracy"], c["medianMs"]))
    winner = sorted_candidates[0]["model"] if sorted_candidates else None

    report = {
        "date": time.strftime("%Y-%m-%d"),
        "fixture": {
            "path": fixture_path,
            "sha256": fixture_hash,
            "questionCount": len(items)
        },
        "chosenDefault": winner,
        "candidates": results
    }

    if output_path:
        with open(output_path, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2, ensure_ascii=False)
        print(f"\nWrote benchmark record to {output_path}")

    print(f"\n==========================================")
    print(f"Chosen Default Model: {winner}")
    print(f"==========================================")

if __name__ == "__main__":
    if len(sys.argv) < 3:
        print("Usage: python3 run-benchmark.py <api_base_url> <candidate_model_1> [candidate_model_2 ...]")
        print("Example: python3 run-benchmark.py http://localhost:5080 qwen2:7b qwen2.5:7b llama3.1:8b")
        sys.exit(1)

    api_url = sys.argv[1]
    candidate_models = sys.argv[2:]
    default_fixture = os.path.join(os.path.dirname(__file__), "../fixtures/purchasing-questions.json")

    run_benchmark(api_url, default_fixture, candidate_models)
