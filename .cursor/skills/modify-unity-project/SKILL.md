---
name: modify-unity-project
description: Modify and review Unity project code with focused changes, Inspector-assigned object references, preserved behavior and comments, Chinese XML documentation for new methods, Unity serialization checks, and clear handoff instructions. Use when the workspace is a Unity project and the user asks to implement, fix, refactor, or review Unity C# code, scenes, prefabs, components, UI, or serialized references.
---

# 修改 Unity 项目

## 确定适用规则

- 仅在当前仓库是 Unity 项目时使用本 Skill。判定依据：存在 `ProjectSettings/ProjectVersion.txt`，或同时存在 `Assets/`、`ProjectSettings/`、`Packages/manifest.json`。
- 先读取仓库及目标路径下适用的 `AGENTS.md`，让更具体的项目规则优先于本 Skill。
- 先判断用户要修改、诊断还是审查；只要求说明或诊断时保持只读。
- 只处理当前问题直接需要的文件和逻辑，不顺手重构、改名、格式化或修改无关内容。
- 保留已有功能、字段、方法和注释，除非修改是解决问题所必需或用户明确要求。
- 遵循目标文件的现有代码风格，不额外添加未被当前问题要求的兜底、兼容或防御性分支。

## 绑定 Unity 对象

- 优先通过 `[SerializeField] private` 字段在 Inspector 中绑定场景对象、组件、UI、Prefab 和其他 Unity 对象。
- 不要默认使用 `GameObject.Find`、`GameObject.FindWithTag`、`FindObjectOfType`、`FindFirstObjectByType`、`transform.Find` 等运行时查找。
- 仅在对象为运行时动态生成、用户明确要求，或场景结构确实无法稳定拖拽绑定时使用 Find 类方法，并说明原因。
- 新增序列化字段时保持项目现有类型和命名风格，例如：

```csharp
[SerializeField] private GameObject targetObject;
```

- 检查新增字段是否需要同步修改 Scene 或 Prefab，并在结果中明确列出所有 Inspector 手动绑定。

## 控制修改边界

- 每个新增方法都在方法签名前添加中文 XML 文档注释，说明方法作用和全部参数含义；无参数方法也要说明作用。
- 不删除已有字段、方法或功能，除非删除是修复所必需，并在最终说明中解释原因。
- 保留用户已有的未提交改动；遇到与任务重叠且无法安全合并的改动时停止并说明冲突。

## 验证修改

- 检查编译错误、序列化字段变化、空引用风险、Scene/Prefab 保存需求和对应 `.meta` 文件。
- 运行与风险相称的现有构建或测试；无法运行 Unity 编辑器验证时明确说明未验证项。
- 不宣称未实际执行的测试、构建或人工验证已经通过。
- 不在仅修改代码的请求中自动执行 Git 提交；提交分析交给 `prepare-git-commit`。

## 输出结果

- 实际修改 Unity 项目代码后，在最终回答最前方添加当前北京时间，格式为 `[北京时间：YYYY-MM-DD HH:mm]`；只做建议、解释或只读审查时不要添加。
- 简要列出修改的文件、行为变化、执行的验证和未验证风险。
- 明确说明是否新增 `[SerializeField]` 字段，以及用户需要在哪些 Scene 或 Prefab 的 Inspector 中完成绑定。
