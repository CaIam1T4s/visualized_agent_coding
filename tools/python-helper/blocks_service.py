"""Compatibility shim for the renamed node projection service.

New code should import `nodes_service`; this module only forwards the old name.
"""
from nodes_service import build_blocks

__all__ = ['build_blocks']
