# Python Helper 协议 v1

## 开发、依赖与发布

- `requirements.txt` 固定 Jedi、Parso 版本及 wheel SHA256；第三方源码不提交到仓库。
- 首次开发执行 `python -B tools/python-helper/scripts/prepare_dependencies.py`，使用该 Python 的 pip 安装到仓库 `artifacts/python-helper/vendor/`，不安装到全局环境。需要 Python 3.10 或更新版本。
- 应用 build/publish 自动执行相同恢复流程；依赖清单不变且依赖已就绪时无需联网。可用 `-p:HelperPython="解释器路径"` 指定构建解释器，运行解释器仍由 `VISUALIZED_AGENT_PYTHON` 配置。
- 离线构建前，可执行 `python -m pip download --require-hashes --only-binary=:all: -r tools/python-helper/requirements.txt -d artifacts/python-helper/wheels`。存在该 wheel 目录时，恢复脚本仅使用本地包；缺包或校验失败会中止构建。
- 运行测试：`python -B -m unittest discover -s tools/python-helper/tests -p "test_*.py"`。
- 发布时将 helper 源码和生成依赖复制到 `python-helper/` 及其 `vendor/`，保留 `.dist-info` 元数据、许可证和 Jedi 随附 Typeshed。普通用户无需执行 pip；Python 解释器仍需另外提供。
- `artifacts/` 为生成目录，已加入 Git 忽略。清空后可通过恢复脚本重新生成。升级依赖时需同时更新版本、wheel 校验值并运行测试。

## 按需引用查询

- `python.findReferences` 需要先握手，能力名为 `findReferences`。参数为 `projectRoot`、`uri`、`version`、`line`、`column`；行列从 1 开始，列为 UTF-16。
- C# 应用层每次收集项目脚本、入口脚本和基础运行时脚本的源码；传入的编辑器文档覆盖磁盘内容，包括空文本。查询不会先保存原始文件。
- C# 基础设施层建立独立临时目录作为本次源码快照。Helper 使用 Jedi 0.20.1 查询作用域、跨文件导入及别名；结果路径映射回原项目。查询成功、失败、取消后，C# 均回收快照。没有写入项目清单的持久化反向引用表。
- 返回 `uri`、`version`、`references`、`issues`、`isIncomplete`。每个位置包含文件 URI、标识符 `range`、`isDefinition` 和 `name`；结果包含定义和导入别名的绑定位置。错误文件会明确标记结果可能不完整。
- 该查询是静态分析，不运行用户脚本；动态导入、运行时生成名称等可能无法完整识别，不可直接视为安全重命名证明。
- 单次最多 2000 个文件、20 MiB 源码、100 个别名查询路径；活动查询默认 30 秒超时。编辑或切换上下文会取消过期查询。Jedi 的内部缓存只位于本次临时快照内，不写用户项目或全局缓存目录。
- 编辑器使用“查找引用”按钮、右键菜单或 Shift+F12，以当前光标位置查询；位置列表可跳转已索引脚本及入口脚本。

## 多错误诊断

- 成功的源码仍由 CPython AST 解析，避免将有效的新语法误判为错误。
- CPython 报错后，通过随应用打包的 Parso 0.8.7 容错解析补充后续错误，按原始行列排序并去重。保留 CPython 的首个错误详情，所有位置均转换为 UTF-16 列。
- 对 CPython 能单独解析的源码块，抑制容错解析器产生的兼容性误报。
- 错误恢复尽力继续解析；未闭合字符串、严重缩进损坏等仍可能遮挡后续错误，无法保证一次列出所有错误。恢复失败时至少保留原始 CPython 错误。
- 编辑器左下角显示错误数量；检查中、未检查和检查不可用分别显示状态，不以零错误代替未知结果。
- 依赖及许可证随 Helper 分发，详见 `requirements.txt`，运行时不需要 pip。

## 传输与消息

- stdin/stdout 每行一条 UTF-8 JSON（无 BOM），以 LF 结束。单条消息包含结尾换行不得超过 8 MiB；源码换行由 JSON 转义。
- stdout 只输出协议消息，stderr 输出日志。C# 持续读取 stderr，只保留末尾 4096 字符，避免管道阻塞和内存增长。
- 请求：`{"jsonrpc":"2.0","id":"1","method":"python.parse","params":{...}}`。id 为连接内递增且不重复的字符串。
- 响应保留相同 id，且恰好包含 result 或 error（整数 code、字符串 message）之一。
- 无 id 的通知不响应。v1 不处理批量请求，不支持请求级协作取消。

## 方法

- `helper.hello`：params 为 `{"protocolVersions":[1]}`；result 包含 protocolVersion、pythonVersion、capabilities（当前 `["parse", "findReferences"]`）。无共同版本返回 -32002。每次启动进程都重新握手。hello 已承担版本和存活确认，不另外增加 ping/version 方法。
- `python.parse`：params 为 `{"uri":"file:///.../example.py","version":1,"source":"..."}`。version 为非负整数。result 原样返回 uri、version，并包含 diagnostics 与 symbols。仅解析，不 import 或执行用户源码。
- `symbols` 包含顶层 classes/functions（name、range）与 imports（module、names、range）；该结果不是完整 AST 或跨文件引用分析。
- 语法错误作为 result.diagnostics 返回（severity、message、line、column），symbols 为 null；解析成功时 diagnostics 为空数组。
- `helper.shutdown`：返回空对象后退出；EOF 同样退出。

## 位置

- 行、列从 1 开始，列按 UTF-16 代码单元计数，与 C# 字符串/AvaloniaEdit 一致。
- range 包含 line、column、endLine、endColumn；结束位置不包含在范围内。
- SyntaxError 的位置从 Python 的字符列转换成 UTF-16；AST 的 UTF-8 字节列也转换成 UTF-16。

## 超时、取消与生命周期

- C# 使用单个懒启动进程，串行处理请求；并发调用在客户端排队，同一进程可以服务多个请求。
- 默认 5 秒请求期限包括排队、进程启动、握手及解析。排队取消不影响其他请求；活动请求超时或取消会杀掉进程及其子进程。
- 异常退出、无响应、非法响应、ID/文档版本不匹配或不兼容握手使本次请求失败；不自动重放失败请求，下一次请求重新启动并握手。
- 关闭客户端时先取消活动请求，再尝试 helper.shutdown（最多 1 秒），最后回收进程及流。
- 默认使用 `python`，可通过 `VISUALIZED_AGENT_PYTHON` 指定可执行文件路径，不接受 shell 命令。启动参数为 `-E -s -B -u`，不修改系统 PATH，不执行用户代码。
- helper 源文件随应用 build/publish 复制到 python-helper/，不依赖运行时能访问源码仓库。Python 尚需由外部环境提供；安装检测和内置环境分发属于后续工作。

## 编辑器诊断

- 输入后防抖 350 毫秒。编辑、切换文档、重命名或关闭项目均递增文档版本并取消旧任务。
- 发布诊断时再次校验请求代次、URI 和文档版本，过期结果不得更新 UI。新输入时清除旧诊断。
- 语法错误、Helper 不可用或超时均保留源码和编辑状态；诊断列表可定位到行列。

## 错误码

- -32700 JSON/UTF-8 解析错误
- -32600 无效请求
- -32601 方法不存在
- -32602 参数无效
- -32603 解析器内部错误
- -32002 协议版本不兼容
- -32004 消息超限（响应后结束进程）
- -32005 未完成握手
