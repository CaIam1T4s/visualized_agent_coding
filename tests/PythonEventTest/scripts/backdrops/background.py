# vac:template-version=3
from runtime.backdrop_base import BackdropBase
# vac:imports-begin
# vac:imports-end


class Background(BackdropBase):
    pass

    # vac:region-begin {"id":"members","kind":"members"}
    # vac:region-end members

    def _vac_on_start(self):
        # vac:region-begin {"id":"start","kind":"start"}
        pass  # 请删除此行并编写代码
        # vac:region-end start

    def _vac_event_message_556df6b2e54f4399b933c8bac568330f(self):
        # vac:region-begin {"id":"message_556df6b2e54f4399b933c8bac568330f","kind":"event","eventName":"message:message"}
        print("background get message")
        # vac:region-end message_556df6b2e54f4399b933c8bac568330f
