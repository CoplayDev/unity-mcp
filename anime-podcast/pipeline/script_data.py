"""
Mobile Frame: EXILE — Episode 1: "Ashfall"

Original work. Characters, setting, and dialogue are wholly invented for this
project so the result is free to publish anywhere. Inspired by the mecha genre
(Gundam et al.) the way any homage is — no copyrighted text or audio is used.

The same scene is authored once (SCENE) and rendered three ways:
  - "pure"      Pure Cut         : dialogue + sound design, no narration
  - "described" Audio-Described  : a narrator paints the action between lines
  - "recap"     Recap Hosts      : two hosts react, with embedded clips
"""

EPISODE = {
    "series": "Mobile Frame: EXILE",
    "number": 1,
    "title": "Ashfall",
    "logline": "A teenage scavenger looking for a spare power cell in a dead "
               "colony instead wakes the most wanted war machine in the Tiers.",
}

# role -> voice model + render/processing options
ROLES = {
    "KESTREL":  {"voice": "ryan",    "ls": 1.0},
    "ECHO":     {"voice": "kristin", "ls": 1.0,  "robot": True},
    "VALE":     {"voice": "alan",    "ls": 1.06, "comm": True},
    "RASK":     {"voice": "joe",     "ls": 1.08, "comm": True},
    "SABINE":   {"voice": "amy",     "ls": 1.0,  "comm": True},
    "NARRATOR": {"voice": "lessac",  "ls": 1.05},
    "HOST_A":   {"voice": "lessac",  "ls": 1.0},
    "HOST_B":   {"voice": "cori",    "ls": 1.0},
}

# display names for the on-screen transcript
NAMES = {
    "KESTREL": "Kestrel", "ECHO": "Echo", "VALE": "Vale", "RASK": "Rask",
    "SABINE": "Compact PA", "NARRATOR": "Narrator",
    "HOST_A": "Delray", "HOST_B": "Pip",
}


# ---- authoring helpers (keep the script readable) ----

def loc(bed):
    return {"t": "loc", "bed": bed}

def fx(name, gain=0.7, pre=0.0):
    return {"t": "fx", "name": name, "gain": gain, "pre": pre}

def act(text, fx=None):
    return {"t": "act", "text": text, "fx": fx or []}

def say(who, text, ls=None, fx=None, id=None):
    return {"t": "say", "who": who, "text": text, "ls": ls, "fx": fx or [], "id": id}

def beat(dur=0.5):
    return {"t": "pause", "dur": dur}


# =====================================================================
#  THE SCENE  (ground truth — pure & described both build from this)
# =====================================================================

SCENE = [
    loc("derelict"),
    act("A dead place. Wind moves through the gutted ribs of Colony Ring Nine, "
        "abandoned twelve years. In the dark, a boy with a wrist lamp picks "
        "through a drift of scrap. His name is Kestrel. He is seventeen, and he "
        "is not supposed to be here."),
    say("KESTREL", "Come on. Come on. There has to be one clean power cell in "
                   "this whole graveyard."),
    beat(0.4),
    act("Far off, a sound that does not belong to a dead colony. A low thud. "
        "Then another, closer.", fx=[fx("explosion", 0.35)]),
    say("KESTREL", "That is not the structure settling."),
    {"t": "fx", "name": "alarm", "gain": 0.45, "pre": 0.1},
    say("SABINE", "Attention. This sector is under Meridian Compact "
                  "jurisdiction. Unauthorized salvage will be met with lethal "
                  "force. You have thirty seconds to comply."),
    say("KESTREL", "Thirty seconds. Great. Generous."),
    act("Kestrel runs. Behind him the corridor lights stutter awake, red, one "
        "by one, as Compact drones flood the ring.", fx=[fx("thruster", 0.4)]),
    say("KESTREL", "The old fabrication bay. They never stripped the "
                   "fabrication bay."),

    loc("hangar"),
    act("He shoulders through a half-sealed blast door into a cavern of black "
        "air. His lamp barely reaches the far wall. And then it catches "
        "something. A shape. Forty meters of dormant steel hung in a cradle of "
        "dead gantries. Humanoid. Sleeping. Waiting.",
        fx=[fx("servo", 0.5), fx("impact", 0.4, pre=0.2)]),
    say("KESTREL", "...Okay. That is definitely not a power cell.", id="frame"),
    act("As he steps closer, something deep in the machine answers.",
        fx=[fx("bootup", 0.6)]),
    say("ECHO", "Cradle integrity nominal. Reactor at four percent, and "
                "climbing. ... Pilot detected."),
    say("KESTREL", "Nope. Nope, nope, nope. I am a scavenger. I scavenge. I do "
                   "not pilot the giant murder machine."),
    say("ECHO", "You are the only living operator in range. Designation of this "
                "unit: Exile. I am its intelligence. You may call me Echo."),
    say("KESTREL", "I may call you a really bad idea."),
    say("ECHO", "Noted. The Compact will breach this bay in ninety seconds. "
                "Statistically, the murder machine improves your outcome.",
        id="echo_dry"),
    {"t": "fx", "name": "comm_open", "gain": 0.4, "pre": 0.2},
    say("RASK", "Compact lance, this is Rask. Heat signature in the fab bay just "
                "spiked. Something down there woke up. ... Burn it."),
    say("KESTREL", "Who is Rask?"),
    say("ECHO", "The man who is about to kill you. Cradle release?"),
    say("KESTREL", "...Do it. Do it, do it, do it."),
    act("The cradle clamps blow. Forty meters of forgotten war drops, catches "
        "itself on screaming thrusters, and stands — for the first time in "
        "twelve years — with a terrified teenager wired into its spine.",
        fx=[fx("servo", 0.6), fx("thruster", 0.7, pre=0.3),
            fx("impact", 0.5, pre=0.6)]),

    loc("cockpit"),
    say("ECHO", "Neural sync at sixty percent. Try not to think too hard. It "
                "listens."),
    say("KESTREL", "It listens to — whoa. Whoa, I felt that. I felt the arm."),
    {"t": "fx", "name": "targeting", "gain": 0.5, "pre": 0.1},
    say("ECHO", "Incoming. Rask's frame. The Harrow. He is the Compact's best. "
                "Please understand what that means."),
    say("RASK", "A child. They woke the Exile for a child. ... This is almost "
                "an insult."),
    act("The Harrow comes out of the dark like a thrown knife.",
        fx=[fx("thruster", 0.5), fx("impact", 0.7, pre=0.4)]),
    say("KESTREL", "He is fast — Echo, he is too fast—"),
    say("ECHO", "Then stop aiming. Stop thinking like a gun. Move like you are "
                "running. You are good at running."),
    say("KESTREL", "...Yeah. Yeah, okay. Running I can do.", id="run"),
    act("And the Exile moves — not like a soldier, but like a kid who has spent "
        "his whole life slipping through gaps that were never meant for him. It "
        "ducks under the Harrow's blade and lets the colony's broken bones do "
        "the rest.", fx=[fx("thruster", 0.7), fx("servo", 0.5, pre=0.3),
                          fx("explosion", 0.5, pre=0.8)]),
    say("RASK", "...Interesting."),
    {"t": "fx", "name": "comm_static", "gain": 0.5, "pre": 0.1},
    say("RASK", "All units, pull back. Mark the frame. We do not lose the Exile "
                "twice."),
    {"t": "fx", "name": "comm_close", "gain": 0.4, "pre": 0.1},

    loc("space"),
    act("They break through the colony's shattered outer ring into open dark. "
        "Earth hangs below, a bruised blue marble. And for one second there is "
        "only the sound of Kestrel breathing.",
        fx=[fx("thruster", 0.5), fx("heartbeat", 0.5, pre=0.4)]),
    say("KESTREL", "...Did we just—"),
    say("ECHO", "Survive? Barely. You are bleeding from a console you "
                "headbutted."),
    say("KESTREL", "Worth it."),
    {"t": "fx", "name": "comm_open", "gain": 0.4, "pre": 0.3},
    say("VALE", "Unregistered Exile-class frame. I have waited twelve years to "
                "hear that reactor sing again. ... I did not expect a kid at the "
                "helm."),
    say("KESTREL", "Who is this?"),
    say("VALE", "My name is Vale. I lead what is left of the people the Compact "
                "tried to erase. And you, son, have just stolen the single most "
                "wanted machine in the Tiers."),
    say("KESTREL", "...I really just wanted a power cell.", id="end"),
    say("VALE", "They always do. Bring it home, Kestrel. We have a great deal to "
                "talk about."),
    {"t": "fx", "name": "comm_close", "gain": 0.4, "pre": 0.1},
    act("Above a dying world, a stolen legend turns toward the dark — and the "
        "boy inside it has no idea he has just become the most hunted person "
        "alive.", fx=[fx("sting", 0.7, pre=0.3)]),
]


# =====================================================================
#  RECAP  (separate host track; {clip: id} pulls a rendered scene line)
# =====================================================================

RECAP = [
    {"t": "fx", "name": "theme", "gain": 0.7},
    say("HOST_A", "Welcome back to Frame Theory, the show where we watch giant "
                  "robots make terrible decisions so you do not have to. I am "
                  "Delray."),
    say("HOST_B", "And I am Pip, and this week — oh, this week. We are cracking "
                  "open Mobile Frame: Exile, episode one, Ashfall. Delray. They "
                  "gave the forty-meter doomsday weapon to a teenager."),
    say("HOST_A", "They gave it to a scavenger, Pip. A kid who is, and I quote—"),
    {"t": "clip", "ref": "frame"},
    say("HOST_B", "That is not a power cell! He is just trying to find a battery "
                  "in a dead colony, and he trips over a war crime in a closet."),
    say("HOST_A", "And can we talk about Echo? Because the ship A.I. has the "
                  "driest delivery I have heard in years. The colony is about to "
                  "be breached, the kid is panicking, and Echo just goes—"),
    {"t": "clip", "ref": "echo_dry"},
    say("HOST_B", "Statistically. I am obsessed. That is my new coworker."),
    say("HOST_A", "Now here is what actually got me. The fight. Rask — the "
                  "Compact's best pilot — he expects a soldier. And instead the "
                  "show does this really smart thing where the kid's only real "
                  "skill is running away."),
    say("HOST_B", "Right, and the A.I. leans into it. It is not become a "
                  "warrior, it is—"),
    {"t": "clip", "ref": "run"},
    say("HOST_A", "It is a fight scene built entirely around the protagonist's "
                  "one talent, which is cowardice with excellent cardio."),
    say("HOST_B", "And then the gut punch. Vale shows up, tells him he just "
                  "stole the most wanted machine in the Tiers, and the kid just—"),
    {"t": "clip", "ref": "end"},
    say("HOST_A", "Best last line of any premiere this season. Episode one, "
                  "Ashfall. We are so in."),
    say("HOST_B", "Next week: apparently the murder machine has opinions. We "
                  "will see you then."),
    {"t": "fx", "name": "sting", "gain": 0.7, "pre": 0.2},
]
