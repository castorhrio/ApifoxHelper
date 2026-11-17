#!/usr/bin/env python3
"""把发布产物 .exe 里由 apphost 重新编码成 Win32 资源的那颗图标抽出来，与 Assets/app.ico 逐字节比对。

为什么要有这一支（第三百六十九轮，控制仓简报 P0-2 / R5）
--------------------------------------------------------
`Assets/app.ico` 的容器结构由 `ApifoxHelper.Tests/AppIconTests.cs` 钉住，`Assets/build-app-icon.py`
负责从两份 SVG 母版重打包它——但**用户双击的那颗 `.exe` 旁边的图标不是这份文件本身**：.NET SDK 的
apphost 会把 `ApplicationIcon` 重新编码成 PE 的 `RT_GROUP_ICON` / `RT_ICON` 资源。这一层此前没有任何
门、也没有任何读数（第三百六十五 / 三百六十六 / 三百六十七轮连续三笔挂账），于是「仓里那份 ico 是对的」
与「用户看到的那颗是对的」之间隔着一个没人看过的重编码步骤。

跑法
----
    dotnet publish ApifoxHelper.csproj -c Release -r win-x64 --self-contained -o /tmp/ah-app-pub
    python3 Assets/check-apphost-icon.py /tmp/ah-app-pub/ApifoxHelper.exe Assets/app.ico

退出码 `0` = 逐字节相同；`1` = 不同（逐条报出差在哪一档）；`2` = 用法错。
**它是一次量测，不是常驻门**：跑它要一次 `dotnet publish`（要还原、要 win-x64 运行时包），
放进 `dotnet test` 会把一条静态门变成一次几分钟的构建。读数记在 `docs/DESIGN.md` §8「品牌资产」那一格
与 `docs/NOTES.md` 对应轮次。

它不判什么
----------
不判这颗图标在任务栏 / 标题栏 / 资源管理器里真机上是什么观感（那要 Windows 真机，G-D5 仍 `[ ]`）；
也不判 `publish.ps1` 那一支的安装包产物（那是另一条路径，另一次构建）。
"""
import struct
import sys


def rva_to_off(sections, rva):
    """RVA → 文件偏移。按各节的 VirtualAddress / SizeOfRawData 找，找不到即报错而不是猜。"""
    for va, vsize, raw_off, raw_size in sections:
        if va <= rva < va + max(vsize, raw_size):
            return raw_off + (rva - va)
    raise SystemExit(f"RVA {rva:#x} 不在任何节里")


def parse_pe(path):
    """读 DOS 头 → PE 头 → 可选头的数据目录第 2 项（资源表）→ 节表。"""
    data = open(path, "rb").read()
    e_lfanew = struct.unpack_from("<I", data, 0x3C)[0]
    if data[e_lfanew:e_lfanew + 4] != b"PE\0\0":
        raise SystemExit(f"{path} 不是 PE 文件")
    coff = e_lfanew + 4
    nsec, = struct.unpack_from("<H", data, coff + 2)
    opt_size, = struct.unpack_from("<H", data, coff + 16)
    opt = coff + 20
    magic, = struct.unpack_from("<H", data, opt)
    dd = opt + (112 if magic == 0x20B else 96)  # PE32+ 与 PE32 的数据目录起点不同
    res_rva, _ = struct.unpack_from("<II", data, dd + 2 * 8)
    sec = opt + opt_size
    sections = []
    for i in range(nsec):
        base = sec + i * 40
        vsize, va = struct.unpack_from("<II", data, base + 8)
        raw_size, raw_off = struct.unpack_from("<II", data, base + 16)
        sections.append((va, vsize, raw_off, raw_size))
    return data, sections, res_rva


def walk(data, sections, res_rva):
    """三层资源目录（类型 → 名字/ID → 语言）走一遍，返回 {(类型, ID): [(语言, RVA, 长度)]}。

    只认数字类型名与数字 ID：字符串名（高位为 1）本仓没有，跳过而不是猜。
    """
    base = rva_to_off(sections, res_rva)
    out = {}

    def entries(off):
        named, ids = struct.unpack_from("<HH", data, off + 12)
        for i in range(named + ids):
            e = off + 16 + i * 8
            name, target = struct.unpack_from("<II", data, e)
            yield name, target

    for t_name, t_target in entries(base):
        if t_name & 0x80000000:
            continue
        t_off = base + (t_target & 0x7FFFFFFF)
        for n_name, n_target in entries(t_off):
            if n_name & 0x80000000:
                continue
            n_off = base + (n_target & 0x7FFFFFFF)
            for l_name, l_target in entries(n_off):
                if l_name & 0x80000000:
                    continue
                leaf = base + l_target
                rva, size = struct.unpack_from("<II", data, leaf)
                out.setdefault((t_name, n_name), []).append((l_name, rva, size))
    return out


def main():
    if len(sys.argv) != 3:
        print("用法：python3 Assets/check-apphost-icon.py <发布产物 .exe> <Assets/app.ico>")
        return 2
    exe, ico = sys.argv[1], sys.argv[2]
    data, sections, res_rva = parse_pe(exe)
    res = walk(data, sections, res_rva)
    groups = sorted(k for k in res if k[0] == 14)  # RT_GROUP_ICON
    icons = sorted(k for k in res if k[0] == 3)    # RT_ICON
    print(f"exe = {exe}")
    print(f"RT_GROUP_ICON 条目：{len(groups)} 个 -> {[k[1] for k in groups]}")
    print(f"RT_ICON 条目：{len(icons)} 个 -> {[k[1] for k in icons]}")
    if not groups:
        raise SystemExit("exe 里没有 RT_GROUP_ICON：apphost 没有把图标编进去")

    # apphost 只编一颗：取第一组，按它自己的 GRPICONDIR 顺序把 RT_ICON 拼回一份 .ico。
    _, rva, size = res[groups[0]][0]
    off = rva_to_off(sections, rva)
    grp = data[off:off + size]
    reserved, typ, count = struct.unpack_from("<HHH", grp, 0)
    print(f"GRPICONDIR：reserved={reserved} type={typ} count={count}")
    entries = []
    for i in range(count):
        e = 6 + i * 14
        w, h, cc, rsv, planes, bits, nbytes, nid = struct.unpack_from("<BBBBHHIH", grp, e)
        entries.append((w, h, cc, rsv, planes, bits, nbytes, nid))
        print(f"  第 {i + 1} 条：{w or 256}x{h or 256} bits={bits} bytes={nbytes} id={nid}")

    blobs = []
    for (w, h, cc, rsv, planes, bits, nbytes, nid) in entries:
        key = (3, nid)
        if key not in res:
            raise SystemExit(f"GRPICONDIR 引用了 RT_ICON {nid}，但 exe 里没有这一条")
        _, irva, isize = res[key][0]
        ioff = rva_to_off(sections, irva)
        blob = data[ioff:ioff + isize]
        if len(blob) != nbytes:
            print(f"  注意：RT_ICON {nid} 实际 {len(blob)} 字节，GRPICONDIR 声明 {nbytes} 字节")
        blobs.append((w, h, cc, rsv, planes, bits, blob))

    out = bytearray(struct.pack("<HHH", 0, 1, len(blobs)))
    offset = 6 + 16 * len(blobs)
    for (w, h, cc, rsv, planes, bits, blob) in blobs:
        out += struct.pack("<BBBBHHII", w, h, cc, rsv, planes, bits, len(blob), offset)
        offset += len(blob)
    for (_, _, _, _, _, _, blob) in blobs:
        out += blob

    ref = open(ico, "rb").read()
    print(f"\n从 exe 抽出的 .ico：{len(out)} 字节")
    print(f"{ico}：{len(ref)} 字节")
    if bytes(out) == ref:
        print("结论：逐字节相同（apphost 编进 .exe 的那颗与 Assets/app.ico 同源）")
        return 0

    print("结论：不逐字节相同，逐条比：")
    _, _, r_count = struct.unpack_from("<HHH", ref, 0)
    print(f"  容器：exe 侧 count={len(blobs)}，app.ico 侧 count={r_count}")
    r_off = 6
    for i in range(min(len(blobs), r_count)):
        rw, rh, _, _, _, rbits, rbytes, roff = struct.unpack_from("<BBBBHHII", ref, r_off)
        w, h, _, _, _, bits, blob = blobs[i]
        same = ref[roff:roff + rbytes] == blob
        print(f"  第 {i + 1} 条：exe {w or 256}x{h or 256} bits={bits} bytes={len(blob)} / "
              f"app.ico {rw or 256}x{rh or 256} bits={rbits} bytes={rbytes} -> "
              f"{'逐字节相同' if same else '不同'}")
        r_off += 16
    return 1


if __name__ == "__main__":
    sys.exit(main())
