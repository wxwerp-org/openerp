import base64
import hashlib
import http.client
import json
import os
from pathlib import Path
import re
import subprocess
import sys
from urllib.parse import urlencode, urlsplit


MANIFEST_TYPES = ", ".join((
    "application/vnd.oci.image.index.v1+json",
    "application/vnd.oci.image.manifest.v1+json",
    "application/vnd.docker.distribution.manifest.list.v2+json",
    "application/vnd.docker.distribution.manifest.v2+json",
))


def registry_get(registry, path, headers, operation):
    address = urlsplit(registry)
    connection_type = (http.client.HTTPSConnection if address.scheme == "https"
                       else http.client.HTTPConnection)
    connection = connection_type(address.netloc, timeout=30)
    try:
        connection.request("GET", path, headers=headers)
        with connection.getresponse() as response:
            return response.status, response.headers, response.read()
    except (OSError, http.client.HTTPException) as error:
        raise RuntimeError(f"{operation}网络请求失败：{error}") from error
    finally:
        connection.close()


def get_registry_token(registry, repository, actor, token):
    credentials = base64.b64encode(f"{actor}:{token}".encode()).decode()
    # 与 Docker 首次推送使用同一 scope，以便在包尚未创建时取得推送权限。
    query = urlencode({"service": urlsplit(registry).netloc,
                       "scope": f"repository:{repository}:pull,push"})
    status, _, body = registry_get(registry, f"/token?{query}",
                                   {"Authorization": f"Basic {credentials}"}, "GHCR 令牌")
    if status != 200:
        raise RuntimeError(f"GHCR 令牌请求失败（HTTP {status}）")
    try:
        registry_token = json.loads(body)["token"]
        if isinstance(registry_token, str) and re.fullmatch(r"[A-Za-z0-9._~+/-]+=*", registry_token):
            return registry_token
    except (ValueError, KeyError, TypeError):
        pass
    raise RuntimeError("GHCR 令牌响应无效")


def get_manifest_digest(registry, repository, tag, token):
    status, headers, body = registry_get(
        registry, f"/v2/{repository}/manifests/{tag}",
        {"Authorization": f"Bearer {token}", "Accept": MANIFEST_TYPES},
        "GHCR manifest",
    )
    if status == 200:
        digest = headers.get("Docker-Content-Digest", "")
        if not re.fullmatch(r"sha256:[0-9a-f]{64}", digest):
            raise RuntimeError("GHCR manifest 响应缺少有效的 SHA-256 摘要")
        return digest
    if status == 404:
        try:
            codes = [error["code"] for error in json.loads(body)["errors"]]
            if codes and all(code in ("NAME_UNKNOWN", "MANIFEST_UNKNOWN") for code in codes):
                return None
        except (ValueError, KeyError, TypeError):
            pass
    raise RuntimeError(f"GHCR manifest 查询失败（HTTP {status}）")


def prepare_base(registry, repository, actor, token, dockerfile):
    recipe = dockerfile.read_bytes()
    tag = "linux-amd64-" + hashlib.sha256(recipe).hexdigest()
    image = f"{urlsplit(registry).netloc}/{repository}"
    registry_token = get_registry_token(registry, repository, actor, token)
    digest = get_manifest_digest(registry, repository, tag, registry_token)
    if digest is None:
        source_repository = repository.rsplit("/", 1)[0]
        # 哈希和构建使用同一份 Dockerfile 内容，且不给 Docker 提供文件系统上下文。
        subprocess.run([
            "docker", "buildx", "build", "--platform", "linux/amd64", "--push",
            "--tag", f"{image}:{tag}",
            "--label", f"org.opencontainers.image.source=https://github.com/{source_repository}",
            "-",
        ], input=recipe, check=True, stdout=sys.stderr)
        registry_token = get_registry_token(registry, repository, actor, token)
        digest = get_manifest_digest(registry, repository, tag, registry_token)
        if digest is None:
            raise RuntimeError("推送结束后 GHCR 中仍没有该基础镜像")
    return f"{image}@{digest}"


if __name__ == "__main__":
    try:
        image = prepare_base(
            "https://ghcr.io", os.environ["GITHUB_REPOSITORY"].lower() + "/openerp-base",
            os.environ["GITHUB_ACTOR"], os.environ.pop("GHCR_TOKEN"), Path("deploy/base.Dockerfile"),
        )
    except (RuntimeError, OSError, subprocess.CalledProcessError) as error:
        sys.exit(str(error))
    print(f"image={image}")
