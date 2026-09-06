---
name: prepare-git-commit
description: Review local Git changes and prepare safe atomic commits by inspecting staged, unstaged, and untracked work; identifying unrelated or risky files; selecting Conventional Commit types and scopes; proposing commit grouping and messages; and performing commits only when explicitly requested. Use when the user says they are ready to commit, asks how to split changes, requests a commit type or message, or explicitly asks to create a local commit.
---

# 准备 Git 提交

## 保持只读边界

- 先读取仓库中的 `AGENTS.md` 和团队提交约定，让仓库规则优先。
- 只请求建议时，仅执行只读检查；不要运行 `git add`、`git commit`、`git push` 或改写历史。
- 仅在用户明确要求提交时暂存并创建本地提交；没有明确要求时不要推送。
- 保留用户已有改动，不还原、覆盖或清理不属于当前任务的文件。

## 检查提交内容

1. 检查 `git status --short`、`git diff --stat`、未暂存 diff 和已暂存 diff。
2. 检查相关未跟踪文件的内容和用途，不要只根据文件名猜测。
3. 识别无关改动、生成文件、调试代码、敏感信息、意外大文件和不应进入版本控制的缓存。
4. 根据行为目标划分可独立理解、验证和回滚的原子提交，不要按文件扩展名机械拆分。
5. 让实现与其直接测试、必要配置和关联元数据保持在同一个完整提交中。
6. 根据改动风险运行现有测试、构建或静态检查；准确报告未执行和失败的验证。

## 选择 Conventional Commit 类型

使用 `<type>(<scope>): <summary>` 格式，并让团队已有规范优先。按主要目的选择：

- `feat`：新增用户可见功能或行为。
- `fix`：修复错误、异常行为或回归。
- `refactor`：调整实现但不改变外部行为。
- `perf`：提升性能、内存或加载效率。
- `test`：仅新增或调整测试。
- `docs`：仅修改文档或说明。
- `style`：仅调整格式、空白或不影响逻辑的代码风格。
- `build`：修改依赖、构建脚本、打包或构建配置。
- `ci`：修改持续集成配置或流水线。
- `chore`：其他不影响产品行为的维护工作。
- `revert`：回滚已有提交。

遵守以下选择规则：

- 行为修复优先使用 `fix`，不要因为同时整理代码就误写为 `refactor`。
- 新功能及其测试、配置和资源通常组成同一个 `feat` 提交。
- 多个可独立审查的目标建议拆分；无法安全拆分时使用最主要的行为类型。
- scope 使用稳定的业务模块或系统名，不使用随意或过细的文件名。
- summary 使用明确动作描述结果，避免 `update code`、`fix bug`、`修改代码` 等空泛标题。
- 在正文解释原因、影响和验证，不要逐行复述 diff。
- 使用 `Refs: #123` 关联事项；使用 `BREAKING CHANGE: ...` 声明破坏性变更。

## 检查 Unity 仓库

仅在仓库是 Unity 项目时追加检查：

- 让资源文件与对应 `.meta` 成对提交，避免意外改变 GUID 和引用。
- 排除 `Library/`、`Temp/`、`Logs/`、`Obj/` 等本地生成目录。
- 检查 Scene、Prefab、ProjectSettings 和 Package 变化是否属于目标行为。
- 新增 `[SerializeField]` 字段时确认对应 Inspector 绑定和 Scene/Prefab 保存是否包含在同一提交。

## 执行明确请求的提交

- 只暂存当前原子提交需要的文件或补丁块，不使用无差别的全量暂存覆盖范围判断。
- 提交前再次检查 `git diff --cached`，确认暂存内容与建议的提交信息一致。
- 创建提交后报告提交哈希、标题、包含范围和验证结果。
- 不修改已共享历史，不使用强制推送；除非用户另行明确要求，否则不要推送。

## 输出建议

按以下顺序输出：

1. **提交结论**：说明当前是否适合提交及阻塞项。
2. **提交拆分**：列出每个建议提交的行为目标和文件范围。
3. **提交信息**：提供可直接使用的标题，必要时补充正文和 footer。
4. **提交前验证**：列出已完成、失败和仍需执行的检查。
5. **手动事项**：列出凭据、Inspector、Scene/Prefab 或发布流程等仍需用户处理的事项。
