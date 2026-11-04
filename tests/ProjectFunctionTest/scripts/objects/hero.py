# vac:template-version=3
from runtime.element_base import ElementBase
# vac:imports-begin
import random  # vac:import {"source":"standard"}
# vac:imports-end


class Hero(ElementBase):
    pass

    # vac:region-begin {"id":"members","kind":"members"}
    def walk(self):
        pass
        
    value = 1
    # vac:region-end members

    def _vac_on_start(self):
        # vac:region-begin {"id":"start","kind":"start"}
        pass
        if True:
            pass
            
        print()
        # vac:region-end start
