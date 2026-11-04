# vac:template-version=3
from runtime.element_base import ElementBase
# vac:imports-begin
# vac:imports-end


class BlueprintTest(ElementBase):
    pass

    # vac:region-begin {"id":"members","kind":"members"}
    # vac:region-end members

    def _vac_on_start(self):
        # vac:region-begin {"id":"start","kind":"start"}

        # gay  
        # pussy
        '''
line
'''
        while True:
            print("1")
        for _ in range(1, 10):
            pass
        if self.x > 0:
            self.x -= 10
        elif self.x < 0:
            self.x + 50
        else:
            '''
    multi
    line
    1
    2
    3
    4
    5
    6
    ''' 
            self.turn(90)
            
            
        match command:
            case "go" if speed > 0:
                speed += 10
            case _:
                pass            # gaygaygaygyayga
            
        # vac:region-end start

    def initialize(self):
        # vac:region-begin {"id":"initialize","kind":"initialize"}
        # init
        self.health = 100
        # vac:region-end initialize
