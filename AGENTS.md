# 项目维护约定

- 项目仓库：`https://github.com/LiHongD/codexQuotadisplay`，主分支 `main`。
- 用户要求：每次完成软件功能更新或修复后，同步更新 GitHub。完成必要验证、更新版本号、`CHANGELOG.md` 和 `releases/v版本号.md`，提交并推送；不需要重复询问是否上传。
- 主分支的软件更新推送会触发 GitHub Actions 构建，生成对应版本 Release、安装包、便携包和 SHA256 校验文件。推送后检查构建和发布结果；失败时修复或明确告知实际阻碍。
- 不上传 `settings.json`、账号登录文件、令牌、真实账号额度、调试日志、`.build` 缓存或原始桌面截图。构建产物通过 Releases 分发，不提交到 Git 历史。
- 历史更新日志是从已有开发记录和版本包整理的；不要为没有对应源码的旧版本伪造提交历史或源码标签。
