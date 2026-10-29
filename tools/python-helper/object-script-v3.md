# 对象脚本 v3 与受管理导入

v3 沿用 v2 的区域 ID、类型、包装缩进及用户原文规则，新增一个模块级受管理导入区。新角色/背景默认使用 v3；旧 v1/v2 必须通过预览确认升级，打开和保存不自动迁移。v1 原用户体保留到 members 并新增 start；v2 全部区域、原文与顺序保持，只更新首行版本并插入空导入区。

```python
# vac:template-version=3
from runtime.element_base import ElementBase
# vac:imports-begin
import scripts.user.tools as utils  # vac:import {"source":"project","scriptId":"script_example"}
import math  # vac:import {"source":"standard"}
# vac:imports-end

class Hero(ElementBase):
    pass
    # 其余区域采用 v2 协议
```

模块级导入区必须在类定义之前。系统必需基类导入仍由模板拥有；受管理区每个语句只包含一个 import 或 from-import 目标、可选别名与真实注释身份元数据。项目目标以清单中的稳定 scriptId 关联；模块路径取实际 Python 语句，因此现有 AST 重命名会更新路径而不会改写 ID。禁止通配符与相对导入。读取时使用 Python 注释 token 与 AST，字符串中的同名文本不构成协议。

普通用户脚本没有对象模板版本。首次管理导入时，在模块文档字符串及 `__future__` 导入之后插入同样的导入区；其余源码原文保留，手写导入仍可编辑。`UserScriptImportProjection` 隐藏该区，显示只读摘要；`MultiRegionProjection` 在角色/背景用户区域之前显示摘要。摘要不参与持久化身份识别，不把实现路径展示成用户编辑内容。无导入的新对象模板不增加摘要行。

## Helper 接口

`python.imports`（能力 `imports`）参数：`uri`、`version`、`source`、`context`；`context` 包含当前 scriptId、kind、项目脚本目录（id/module/kind/displayName）及可信用户区域 UTF-16 范围。可选 `imports` 表示期望的完整导入列表，可选 `convertLine` 将指定原始行的独立手写导入迁入受管理区。接口只返回候选源码，不写文件、不导入或执行用户模块。

结果：`source`、`block`（UTF-16 span、entries）、`standardModules`、`diagnostics`、`syntaxValid`、`insertOffset`、`problem` 及文档身份。entry 字段为 module、可选 name、可选 alias、source（project/standard）、可选 scriptId（项目目标必需）。无受管理导入区时 block 为 null。目录和版本结果必须由调用方复检。

`python.parse` 可选 `importContext` 使用相同上下文，追加 category=`import-rule` 的规则诊断；navigation=`imports` 表示点击应打开导入管理。原生语法错误保留；源码语法不完整时不猜测手写导入规则，但已存在的受管理导入仍校验目标。规则与语法诊断统一统计并分别标识。

## 来源与策略

- 项目目标只允许其他普通用户脚本，拒绝角色、背景和自身。检查目标存在、路径/ID 一致、名称冲突及重复导入。
- 标准库目录由实际 Helper Python 的 stdlib 清单及内置/冻结/文件规格获取，不执行目标模块，不运行 pip。拒绝被项目同名脚本遮蔽的标准库选择。
- 内部 runtime/vac_runtime 模块禁止用户导入；系统必需依赖不受用户操作影响。当前产品没有可选公开运行时 API，界面该分类显示暂无可用项，不虚构包或安装环境。
- 第三方包管理尚未实现；未知外部模块报告导入规则错误。动态表达式不是完整安全边界；Python 运行器使用独立执行进程，运行前复用这些静态校验。
- 角色/背景用户区域中的真实 import/from-import 提示通过菜单管理；普通用户脚本保留手写导入但校验目标。
- 转换仅接受独立单行导入，保留目标、符号、别名及原行注释。若移除后方法体为空，留下可编辑 pass；同行复合语句和多行导入先由用户拆分，不冒险删除其他逻辑。

## 应用与恢复

“更多 → 添加 → 导入脚本…”和“管理导入…”使用主窗口内浮层，列表修改直到确认才应用；取消不修改源码。候选应用前复检项目、脚本、文档版本及源码。结构变更与原生文本共同撤销重做；普通保存不清空历史。导入修改不立即写盘；保存、导航与退出复用已有冲突检查及自动保存策略。

项目重命名同步真实导入路径，删除影响检查仍分析完整源码，删除后保留引用方。导入移除或别名修改不会自动改写调用，浮层明确提示用户自行调整。导入区损坏时保留完整原文并提供只读恢复/重新加载入口。

运行按钮在现有运行器占位之前检查当前脚本的导入规则；实际执行及依赖闭包的运行前检查将由后续运行器接入。

Helper 不可用时，无受管理标记的普通脚本保持可编辑；已有导入区无法确认时保留完整源码并进入只读恢复，避免绕过导入身份保护。
