#!/usr/bin/env python3
"""
Лише для перевірки ручного розгортання (у кластері немає Auth Service): видає JWT у форматі Auth Service (Лаб. 09),
підписаний тим самим ключем, що лежить у Secret gateway-secrets / books-secrets.
  python3 deploy/k8s/dev-token.py "<JWT_SECRET>" admin   -> рядок токена
"""
import base64, hashlib, hmac, json, sys, time, uuid

secret, role = sys.argv[1], (sys.argv[2] if len(sys.argv) > 2 else "admin")
b64 = lambda b: base64.urlsafe_b64encode(b).rstrip(b"=").decode()
now = int(time.time())
header = {"alg": "HS256", "typ": "JWT"}
payload = {"sub": str(uuid.uuid4()), "unique_name": f"k8s-{role}", "role": [role], "jti": str(uuid.uuid4()),
           "iss": "library-auth-service", "aud": "library-platform", "iat": now, "nbf": now, "exp": now + 900}
signing_input = f"{b64(json.dumps(header).encode())}.{b64(json.dumps(payload).encode())}"
signature = hmac.new(secret.encode(), signing_input.encode(), hashlib.sha256).digest()
print(f"{signing_input}.{b64(signature)}")
