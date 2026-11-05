"""Decode FeatureScript values returned by the /featurescript evaluation endpoint.

Values arrive as typed JSON (BTFSValueMap, BTFSValueArray, BTFSValueWithUnits, ...).
This turns them into plain Python: maps -> dict, arrays -> list, lengths/areas/volumes
-> float in SI units (metres, m², m³), undefined -> None.
"""


def decode(value):
    if value is None:
        return None
    if not isinstance(value, dict):
        return value
    bt = value.get("btType") or value.get("typeName") or ""
    inner = value.get("message", value).get("value") if "message" in value else value.get("value")

    if bt.endswith("BTFSValueMap"):
        return {_key(entry["key"]): decode(entry["value"]) for entry in inner or []}
    if bt.endswith("BTFSValueArray"):
        return [decode(v) for v in inner or []]
    if bt.endswith("BTFSValueUndefined"):
        return None
    # Number, String, Boolean, WithUnits, Enum: the payload is in "value"
    return inner


def _key(key):
    k = decode(key)
    return k if isinstance(k, str) else str(k)
