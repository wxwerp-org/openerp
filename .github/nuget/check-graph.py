"""检查锁定还原生成的每个项目、目标框架和运行时的最终 NuGet 依赖图。"""

import json
import sys
from fnmatch import fnmatchcase
from pathlib import Path
from xml.etree import ElementTree


def main() -> int:
    patterns = Path(__file__).with_name("dotnet-packages.txt").read_text().lower().splitlines()
    projects = ElementTree.parse("OpenERP.slnx").findall(".//Project")
    if not projects:
        sys.exit("OpenERP.slnx 中没有可检查的项目")

    compliant = True
    target_count = 0
    for entry in projects:
        project = Path(entry.attrib["Path"])
        assets = project.parent / "obj" / "project.assets.json"
        targets = json.loads(assets.read_text())["targets"]
        if not targets:
            sys.exit(f"{assets} 中没有已还原的目标")
        for target, libraries in targets.items():
            target_count += 1
            for key, library in libraries.items():
                if library["type"] != "package":
                    continue
                name, version = key.split("/")
                major = int(version.split(".", 1)[0])
                if major > 10 and any(fnmatchcase(name.lower(), pattern) for pattern in patterns):
                    print(f"{project} [{target}] 违反 .NET 10 版本策略：{name} {version}")
                    compliant = False

    print(f"已检查 {len(projects)} 个项目、{target_count} 个还原目标")
    return 0 if compliant else 1


if __name__ == "__main__":
    sys.exit(main())
