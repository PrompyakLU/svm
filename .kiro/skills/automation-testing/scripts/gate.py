"""Approval_Gate / lifecycle state machine.

Deterministic reference implementation of the lifecycle gate logic
described in the design (see "Lifecycle and Approval Gates"). The orchestrator
prose in SKILL.md mirrors this module; the module exists so the gate state
machine is executable and property-testable.

Canonical ordered stages (Req 2.1):

    Intake -> Analyze -> Generate -> ValidateOne -> ScaleSuite -> Report -> ReRun

Each stage ends at an Approval_Gate. The orchestrator never auto-advances
across a gate: it pauses in ``AwaitingApproval`` until an explicit user event.

Events (Req 7.2, 7.3, 7.4):
    - ``work_complete``: Running -> AwaitingApproval (stage work finished).
    - ``pause``:   stays AwaitingApproval, stage_index unchanged (Req 7.2).
    - ``change``:  stays same stage_index, returns to AwaitingApproval so the
                   same gate is re-presented (Req 7.3).
    - ``proceed``: advances stage_index to the canonical successor only,
                   clamped at the terminal stage (Req 7.4, 2.1).
"""

from __future__ import annotations

from dataclasses import dataclass

# Canonical ordered lifecycle stages (Req 2.1). Intake and Analyze are SEPARATE
# stages: Intake gathers inputs from the user; Analyze then performs the detailed
# project analysis. This matches the seven skill-library skills and the router.
STAGES: tuple[str, ...] = (
    "Intake",
    "Analyze",
    "Generate",
    "ValidateOne",
    "ScaleSuite",
    "Report",
    "ReRun",
)

# Phase values.
RUNNING = "Running"
AWAITING_APPROVAL = "AwaitingApproval"

# Event names.
EVENT_WORK_COMPLETE = "work-complete"
EVENT_PAUSE = "pause"
EVENT_CHANGE = "change"
EVENT_PROCEED = "proceed"

EVENTS: tuple[str, ...] = (
    EVENT_WORK_COMPLETE,
    EVENT_PAUSE,
    EVENT_CHANGE,
    EVENT_PROCEED,
)


@dataclass
class GateState:
    """State of the Approval_Gate state machine.

    Attributes:
        stage_index: Position in the canonical ``STAGES`` list.
        phase: Either ``Running`` or ``AwaitingApproval``.
    """

    stage_index: int = 0
    phase: str = RUNNING

    def __post_init__(self) -> None:
        if not 0 <= self.stage_index < len(STAGES):
            raise ValueError(f"stage_index out of range: {self.stage_index}")
        if self.phase not in (RUNNING, AWAITING_APPROVAL):
            raise ValueError(f"invalid phase: {self.phase}")

    @property
    def stage(self) -> str:
        """Canonical stage name for the current index."""
        return STAGES[self.stage_index]

    @property
    def is_terminal(self) -> bool:
        """True when the current stage is the last canonical stage."""
        return self.stage_index == len(STAGES) - 1


def successor_index(stage_index: int) -> int:
    """Return the canonical successor index, clamped at the terminal stage.

    Advancing a non-terminal stage yields ``stage_index + 1``; the terminal
    stage stays put (clamp).
    """
    if not 0 <= stage_index < len(STAGES):
        raise ValueError(f"stage_index out of range: {stage_index}")
    if stage_index == len(STAGES) - 1:
        return stage_index
    return stage_index + 1


def transition(state: GateState, event: str) -> GateState:
    """Apply ``event`` to ``state`` and return the resulting ``GateState``.

    Transition rules:
        - ``work-complete``: Running -> AwaitingApproval (stage unchanged).
        - ``pause``:   AwaitingApproval -> AwaitingApproval (stage unchanged).
        - ``change``:  AwaitingApproval -> AwaitingApproval, same stage
                       (re-present the same gate).
        - ``proceed``: AwaitingApproval -> Running at the canonical successor
                       stage (clamped at terminal).

    The function is pure: it returns a new ``GateState`` and never mutates the
    input.
    """
    if event not in EVENTS:
        raise ValueError(f"unknown event: {event}")

    if event == EVENT_WORK_COMPLETE:
        # Stage work finished -> stop at the gate. Stage index never changes.
        return GateState(stage_index=state.stage_index, phase=AWAITING_APPROVAL)

    if event == EVENT_PAUSE:
        # No user response yet: remain awaiting approval, same stage (Req 7.2).
        return GateState(stage_index=state.stage_index, phase=AWAITING_APPROVAL)

    if event == EVENT_CHANGE:
        # Apply edit, re-present the same gate: same stage, AwaitingApproval
        # (Req 7.3).
        return GateState(stage_index=state.stage_index, phase=AWAITING_APPROVAL)

    # EVENT_PROCEED: advance to the canonical successor only (Req 7.4, 2.1).
    next_index = successor_index(state.stage_index)
    return GateState(stage_index=next_index, phase=RUNNING)
