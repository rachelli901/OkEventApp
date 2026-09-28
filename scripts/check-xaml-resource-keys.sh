#!/usr/bin/env bash
#
# check-xaml-resource-keys.sh
#
# XAML 资源字典「键定义 / 键引用」双向差集校验。
#
# ── 为什么必须有这一步 ────────────────────────────────────────────────
# MAUI 的 XamlC 与 SourceGen **都不校验 `StaticResource` 的 key 是否存在**。
# 键名写错时编译期照样 0 错误 / 0 警告，直到运行时解析页面才抛
# XamlParseException —— 症状是「页面直接打不开」，属于最严重的一档故障。
# 本项目已经踩过一次，见 docs/OkEventApp-技术文档.md §9.5。
#
# ── 检查两个方向 ──────────────────────────────────────────────────────
#   ① 引用了但未定义（A - B）  → 致命，运行时崩溃        → 退出码 1
#   ② 定义了但未引用（B - A）  → 冗余，不改坏任何行为    → 仅告警，退出码 0
#
# ② 之所以不阻断：资源字典里本就存在大量「模板自带、暂时没用到」的控件样式，
# 一旦阻断，CI 会被这些无害条目淹没，反而掩盖 ① 类真实故障。
#
# ── 用法 ──────────────────────────────────────────────────────────────
#   bash scripts/check-xaml-resource-keys.sh
#
# 必须以仓库根目录为 cwd 运行。需要 grep / awk / sed / sort / comm。
# Git Bash（Windows）与 POSIX shell、Linux CI runner 均可运行。
#
# 退出码：0 = 通过；1 = 发现「引用了但未定义」；2 = 用法或环境错误。

set -uo pipefail

# 单工程布局，源码在 OkEventApp/ 下（不是仓库根）
readonly PROJECT_DIR="OkEventApp"
readonly VIEW_DIR="$PROJECT_DIR/Views"
readonly STYLE_DIR="$PROJECT_DIR/Resources/Styles"

readonly RED=$'\033[0;31m'
readonly YELLOW=$'\033[0;33m'
readonly GREEN=$'\033[0;32m'
readonly RESET=$'\033[0m'

die() { printf '%s错误：%s%s\n' "$RED" "$1" "$RESET" >&2; exit 2; }

# ── 0. 环境与依赖自检 ─────────────────────────────────────────────────
for tool in grep awk sed sort comm; do
  command -v "$tool" >/dev/null 2>&1 || die "缺少依赖命令：$tool"
done

[[ -d "$VIEW_DIR" ]]  || die "找不到 $VIEW_DIR，请以仓库根目录为 cwd 运行"
[[ -d "$STYLE_DIR" ]] || die "找不到 $STYLE_DIR，请以仓库根目录为 cwd 运行"

shopt -s nullglob

# ── 1. 抽取「引用」侧 ─────────────────────────────────────────────────
# 抓 `StaticResource <Key>` 的 Key。
# 关键：先用 awk 剥离跨行 <!-- --> 注释，否则注释里写的示例键名会被误判成引用。
extract_referenced() {
  awk '
    function find(s, t, start,   i, n) {
      n = length(t)
      for (i = start; i + n - 1 <= length(s); i++)
        if (substr(s, i, n) == t) return i
      return 0
    }
    function strip(s,   out, i, p) {
      out = ""; i = 1
      while (i <= length(s)) {
        if (substr(s, i, 4) == "<!--") {
          p = find(s, "-->", i + 4)
          i = (p == 0) ? length(s) + 1 : p + 3
          continue
        }
        out = out substr(s, i, 1); i++
      }
      return out
    }
    {
      line = strip($0)
      while (match(line, /StaticResource[[:space:]]+[A-Za-z_][A-Za-z0-9_]*/)) {
        tok = substr(line, RSTART, RLENGTH)
        sub(/^StaticResource[[:space:]]+/, "", tok)
        print tok
        line = substr(line, RSTART + RLENGTH)
      }
    }
  ' "$1"
}

# ── 2. 抽取「定义」侧 ─────────────────────────────────────────────────
# 抓 `x:Key="..."` 的 Key，同样先剥离注释。
extract_defined() {
  awk '
    function find(s, t, start,   i, n) {
      n = length(t)
      for (i = start; i + n - 1 <= length(s); i++)
        if (substr(s, i, n) == t) return i
      return 0
    }
    function strip(s,   out, i, p) {
      out = ""; i = 1
      while (i <= length(s)) {
        if (substr(s, i, 4) == "<!--") {
          p = find(s, "-->", i + 4)
          i = (p == 0) ? length(s) + 1 : p + 3
          continue
        }
        out = out substr(s, i, 1); i++
      }
      return out
    }
    {
      line = strip($0)
      while (match(line, /x:Key="[^"]*"/)) {
        tok = substr(line, RSTART, RLENGTH)
        sub(/^x:Key="/, "", tok)
        sub(/"$/, "", tok)
        print tok
        line = substr(line, RSTART + RLENGTH)
      }
    }
  ' "$1"
}

referenced_tmp=$(mktemp)
defined_tmp=$(mktemp)
cleanup() { rm -f "$referenced_tmp" "$defined_tmp"; }
trap cleanup EXIT

view_files=($VIEW_DIR/*.xaml)
style_files=($STYLE_DIR/*.xaml)
(( ${#view_files[@]} > 0 ))  || die "$VIEW_DIR 下没有 .xaml 文件"
(( ${#style_files[@]} > 0 )) || die "$STYLE_DIR 下没有 .xaml 文件"

for f in "${view_files[@]}";  do extract_referenced "$f" >> "$referenced_tmp"; done
for f in "${style_files[@]}"; do extract_defined   "$f" >> "$defined_tmp"; done

sort -u "$referenced_tmp" -o "$referenced_tmp"
sort -u "$defined_tmp"    -o "$defined_tmp"

# 引用侧为空说明抽取逻辑 itself 坏了（改坏了正则 / 路径），必须拦住，
# 否则「差集为空」会被误读成校验通过。
if [[ ! -s "$referenced_tmp" ]]; then
  die "抽取到的引用集合为空 —— 抽取逻辑或路径已失效，拒绝以「空差集」蒙混过关"
fi

# ── 3. 方向①：引用了但未定义（致命） ──────────────────────────────────
missing=$(comm -23 "$referenced_tmp" "$defined_tmp")
missing_count=$(comm -23 "$referenced_tmp" "$defined_tmp" | wc -l)

# ── 4. 方向②：定义了但未引用（仅告警） ────────────────────────────────
unused=$(comm -13 "$referenced_tmp" "$defined_tmp")
unused_count=$(comm -13 "$referenced_tmp" "$defined_tmp" | wc -l)

printf '资源键校验：定义 %s 个 / 引用 %s 个\n' "$(wc -l < "$defined_tmp")" "$(wc -l < "$referenced_tmp")"

if (( missing_count > 0 )); then
  printf '\n%s引用了但未定义（%s 个）—— 运行时会抛 XamlParseException，页面打不开：%s\n' \
    "$RED" "$missing_count" "$RESET"
  printf '%s\n' "$missing" | sed 's/^/  ✗ /'
  printf '\n修复：把 Colors.xaml 的 x:Key 改成上面这些名字，或补上对应定义。\n'
  exit 1
fi

printf '%s方向①通过：不存在「引用了但未定义」的键%s\n' "$GREEN" "$RESET"

if (( unused_count > 0 )); then
  printf '\n%s方向②提示：定义了但未被引用（%s 个，不阻断构建）：%s\n' \
    "$YELLOW" "$unused_count" "$RESET"
  printf '%s\n' "$unused" | sed 's/^/  · /'
  printf '\n这些是冗余定义，清理与否不影响运行，可择机删除。\n'
else
  printf '%s方向②通过：不存在冗余定义%s\n' "$GREEN" "$RESET"
fi

exit 0
