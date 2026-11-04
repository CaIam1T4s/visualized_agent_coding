# vac:template-version=3
from runtime.element_base import ElementBase
# vac:imports-begin
# vac:imports-end


class V2(ElementBase):
    pass

    # vac:region-begin {"id":"members","kind":"members"}
    def walk(self):
        self.move(10)

    def my_method(self):
        pass  # 请删除此行并编写代码
    # vac:region-end members

    def _vac_on_start(self):
        # vac:region-begin {"id":"start","kind":"start"}
        pass
        # vac:region-end start

    def initialize(self):
        # vac:region-begin {"id":"initialize","kind":"initialize"}
        pass
        # vac:region-end initialize
