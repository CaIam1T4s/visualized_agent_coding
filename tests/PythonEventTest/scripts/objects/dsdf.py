# vac:template-version=3
from runtime.element_base import ElementBase
# vac:imports-begin
from vac_runtime import get_variable  # vac:import {"source":"runtime"}
from vac_runtime import set_variable  # vac:import {"source":"runtime"}
from vac_runtime import change_variable  # vac:import {"source":"runtime"}
from vac_runtime import list_length  # vac:import {"source":"runtime"}
from vac_runtime import list_clear  # vac:import {"source":"runtime"}
from vac_runtime import list_remove  # vac:import {"source":"runtime"}
from vac_runtime import list_set  # vac:import {"source":"runtime"}
from vac_runtime import list_get  # vac:import {"source":"runtime"}
from vac_runtime import list_append  # vac:import {"source":"runtime"}
# vac:imports-end


class Dsdf(ElementBase):
    pass

    # vac:region-begin {"id":"members","kind":"members"}
    # vac:region-end members
    # 已导入：vac_runtime.get_variable、vac_runtime.set_variable、vac_runtime.change_variable、vac_runtime.list_length、vac_runtime.list_clear、vac_runtime.list_remove、vac_runtime.list_set、vac_runtime.list_get、vac_runtime.list_append

    def _vac_on_start(self):
        # vac:region-begin {"id":"start","kind":"start"}
        self.goto(100, 0)
        self.x -= 20
        # vac:region-end start

