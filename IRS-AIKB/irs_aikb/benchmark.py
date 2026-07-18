"""Transparent peer benchmarking using supplied, versioned comparison cells."""
from __future__ import annotations
from math import isfinite
from statistics import median

MIN_PEERS = 20

def benchmark(value: float, peers: list[float], *, benchmark_id: str) -> dict:
    clean = sorted(float(x) for x in peers if not isinstance(x, bool) and isfinite(float(x)))
    if len(clean) < MIN_PEERS:
        return {"benchmark_id": benchmark_id, "status": "not_assessable",
                "sample_size": len(clean), "minimum_sample_size": MIN_PEERS}
    center = median(clean); deviations = [abs(x-center) for x in clean]; mad = median(deviations)
    percentile = 100 * sum(x <= value for x in clean) / len(clean)
    robust_z = None if mad == 0 else 0.6745 * (value-center) / mad
    return {"benchmark_id": benchmark_id, "status": "assessable", "sample_size": len(clean),
            "median": center, "mad": mad, "percentile": round(percentile, 2),
            "robust_z": None if robust_z is None else round(robust_z, 3),
            "outlier_flag": robust_z is not None and abs(robust_z) >= 3.5,
            "method": "median_MAD", "warning": "Peer anomaly is a review signal, not proof of error."}

def benchmark_features(features: dict, cells: dict) -> list[dict]:
    return [benchmark(float(features[name]), cell["peers"], benchmark_id=cell["benchmark_id"])
            | {"feature_name": name} for name, cell in cells.items()
            if features.get(name) is not None]
