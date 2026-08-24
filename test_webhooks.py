import urllib.request
import urllib.error
import json

def test_get(url):
    try:
        with urllib.request.urlopen(url) as response:
            print(f"GET {url} -> {response.status} {response.read().decode('utf-8')}")
    except urllib.error.HTTPError as e:
        print(f"GET {url} -> {e.code} {e.reason}")

def test_post(url, payload):
    data = json.dumps(payload).encode('utf-8')
    req = urllib.request.Request(url, data=data, headers={'Content-Type': 'application/json'}, method='POST')
    try:
        with urllib.request.urlopen(req) as response:
            print(f"POST {url} -> {response.status} {response.read().decode('utf-8')}")
    except urllib.error.HTTPError as e:
        print(f"POST {url} -> {e.code} {e.reason}")

base_url = "http://localhost:5000/api/webhook/instagram"

print("1. Valid verification:")
test_get(f"{base_url}?hub.mode=subscribe&hub.verify_token=instarag_verify_2024&hub.challenge=challenge123")

print("\n2. Invalid verification:")
test_get(f"{base_url}?hub.mode=subscribe&hub.verify_token=wrong_token&hub.challenge=challenge123")

print("\n3. POST message payload:")
payload = {
    "object": "instagram",
    "entry": [
        {
            "id": "entry_1",
            "time": 1700000000,
            "messaging": [
                {
                    "sender": {"id": "user_123"},
                    "recipient": {"id": "page_456"},
                    "timestamp": 1700000000,
                    "message": {
                        "mid": "mid_abc_123",
                        "text": "Do you have sneakers?"
                    }
                }
            ]
        }
    ]
}
test_post(base_url, payload)
