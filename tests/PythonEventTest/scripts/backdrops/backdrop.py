# vac:template-version=3
from runtime.backdrop_base import BackdropBase
# vac:imports-begin
# vac:imports-end


class Backdrop(BackdropBase):
    pass

    # vac:region-begin {"id":"members","kind":"members"}
    # vac:region-end members

    def _vac_on_start(self):
        # vac:region-begin {"id":"start","kind":"start"}
        self.activate()
        # vac:region-end start

    def _vac_event_stage_click_027ee3bfdbde492e87ac39b110aa3980(self):
        # vac:region-begin {"id":"stage_click_027ee3bfdbde492e87ac39b110aa3980","kind":"event","eventName":"stage-click"}
        print("background")
        # vac:region-end stage_click_027ee3bfdbde492e87ac39b110aa3980


