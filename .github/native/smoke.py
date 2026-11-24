"""从真实的原生 Aspire 入口启动两次，通过前端验证首次设置和账号持久化。"""
import http.cookiejar
import json
import os
import secrets
import subprocess
import tempfile
import urllib.request


def cli(*args, capture=False):
    return subprocess.run(["aspire", *args, "--non-interactive"], check=True,
                          stdout=subprocess.PIPE if capture else None, text=True).stdout


def probe(password, first_run):
    cli("wait", "web", "--timeout", "180")
    resources = json.loads(cli("describe", "--format", "json", capture=True))["resources"]
    assert all(resource["resourceType"] != "Container" for resource in resources)
    web = next(resource for resource in resources if resource["displayName"] == "web")
    url = next(endpoint["url"] for endpoint in web["urls"] if endpoint["url"].startswith("https://"))
    browser = urllib.request.build_opener(urllib.request.ProxyHandler({}),
                                          urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    with browser.open(url, timeout=15) as response:
        assert response.status == 200 and b'id="app"' in response.read()

    def api(path, body=None, token=None):
        headers = {"Content-Type": "application/json"}
        if token:
            headers["X-CSRF-TOKEN"] = token
        request = urllib.request.Request(url.rstrip("/") + "/api/auth/" + path,
                                         None if body is None else json.dumps(body).encode(), headers)
        with browser.open(request, timeout=15) as response:
            return response.status, response.read()

    assert json.loads(api("status")[1])["initialized"] == (not first_run)
    csrf = json.loads(api("csrf")[1])["requestToken"]
    if first_run:
        assert api("initialize", {"newPassword": password}, csrf)[0] == 204
    assert api("login", {"username": "admin", "password": password}, csrf)[0] == 200


with tempfile.TemporaryDirectory(prefix="openerp-native-") as directory:
    os.environ["OpenERP__DataDirectory"] = directory
    password = secrets.token_hex(9)
    for first_run in (True, False):
        try:
            cli("start", "--isolated", "--launch-profile", "native", capture=True)
            probe(password, first_run)
        finally:
            cli("stop")
    print("原生启动、首次设置、重开和再次登录均通过")
