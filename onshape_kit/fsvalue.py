"""Decode FeatureScript values returned by the /featurescript evaluation endpoint.

Values arrive as typed JSON: {"typeName": "BTFSValueMap", "message": {"value": [...]}}, where
map items are BTFSValueMapEntry {"message": {"key": ..., "value": ...}}. This turns them into
plain Python: maps -> dict, arrays -> list, lengths/areas/volumes -> float in SI units
(metres, m², m³), undefined -> None.
"""


def decode(value):
    if not isinstance(value, dict):
        return value
    kind = value.get("typeName") or value.get("btType") or ""
    body = value.get("message", value)
    inner = body.get("value")

    if kind.endswith("BTFSValueMap"):
        out = {}
        for entry in inner or []:
            e = entry.get("message", entry)
            key = decode(e["key"])
            out[key if isinstance(key, str) else str(key)] = decode(e["value"])
        return out
    if kind.endswith("BTFSValueArray"):
        return [decode(v) for v in inner or []]
    if kind.endswith("BTFSValueUndefined"):
        return None
    # Number, String, Boolean, WithUnits, Enum: the payload is in "value"
    return inner
