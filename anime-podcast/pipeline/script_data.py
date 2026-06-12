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


# =====================================================================
#  FRAME THEORY — commentary episodes about real anime.
#
#  These are transformative commentary / criticism: two original hosts
#  give their own analysis and opinions about famous franchises. No
#  copyrighted dialogue, script, or audio is reproduced — the shows are
#  named and discussed the way any review podcast names and discusses
#  them. Nothing here is a transcript of, or substitute for, the source
#  works.
# =====================================================================

GUNDAM_RECAP = [
    {"t": "fx", "name": "theme", "gain": 0.7},
    say("HOST_A", "Welcome back to Frame Theory. I am Delray, and today we are "
                  "not cracking open a new show — we are going back to the one "
                  "that started literally all of this. Nineteen seventy-nine. "
                  "Mobile Suit Gundam."),
    say("HOST_B", "I am Pip, and here is the thesis, right up front: this is the "
                  "show that split the entire giant-robot world in half. Before "
                  "it, robots were superheroes. After it, robots were equipment."),
    say("HOST_A", "That is the whole revolution in one word. Equipment. The "
                  "mobile suits are mass-produced military hardware. They run out "
                  "of ammo. They need maintenance. They get assigned to scared "
                  "teenagers who were drafted into a war they did not start."),
    say("HOST_B", "Amuro is not a chosen one. He is a civilian kid who climbs "
                  "into the prototype because the trained adults around him are "
                  "already dead and somebody has to pull the lever."),
    say("HOST_A", "And then there is the rival. The masked antagonist basically "
                  "invented an archetype that the next forty-five years of anime "
                  "are still paying rent on. The tragic grudge, the custom "
                  "color-coded unit, the soldier who is too charismatic to be a "
                  "villain and too bitter to be a hero."),
    say("HOST_B", "They built a calendar, Delray. A whole fake history — the "
                  "Universal Century — so internally consistent that grown adults "
                  "now write essays about the economics of space colonies."),
    say("HOST_A", "And underneath the war story there is this quiet, almost "
                  "spiritual idea — that living out in space might be slowly "
                  "evolving human empathy into something close to psychic. The "
                  "show is really asking whether war is just a failure of people "
                  "to understand each other."),
    say("HOST_B", "Does it all hold up? Honestly, no. The pacing is nineteen "
                  "seventy-nine television pacing. There is a monster-of-the-week "
                  "skeleton bolted onto an anti-war epic, and you can feel the "
                  "bolts."),
    say("HOST_A", "But when it lands — the moment a child soldier realizes the "
                  "pilot he just shot down was a person with a name and a family "
                  "— that single beat is the genetic code of every serious mecha "
                  "show since. Macross. Evangelion. The original drama we run on "
                  "this very feed."),
    say("HOST_B", "Ride recommendation: do not binge it like a modern series. "
                  "Let it breathe. One or two a day, like reading a war diary."),
    say("HOST_A", "That is the real robot revolution. Robots stopped being gods "
                  "and started being machines that people have to live and die "
                  "inside. Next time, we go loud. Very loud."),
    {"t": "fx", "name": "sting", "gain": 0.7, "pre": 0.2},
]

GUNDAM_PRIMER = [
    {"t": "fx", "name": "theme", "gain": 0.6},
    {"t": "fx", "name": "comm_open", "gain": 0.3},
    say("NARRATOR", "A Frame Theory primer, for the road. No spoilers that "
                    "matter — just enough to ride in knowing why this one is a "
                    "pillar."),
    say("NARRATOR", "The original Mobile Suit Gundam is the founding text of "
                    "what fans call the real robot genre. The giant machines are "
                    "not magic. They are tanks with arms — mass-produced, "
                    "fuel-hungry, and only as good as the frightened young pilots "
                    "strapped inside them."),
    say("NARRATOR", "The story follows a teenage civilian thrown behind the "
                    "controls of an experimental unit when his colony is caught "
                    "in a war between Earth and its breakaway space settlements."),
    say("NARRATOR", "Its lasting ideas are simple and heavy. War is logistics "
                    "and grief, not glory. The enemy is a person. And a new kind "
                    "of human, sharpened by life in space, might just be able to "
                    "feel that across a battlefield."),
    say("NARRATOR", "If you only know the genre through its descendants, this is "
                    "the source of the river. Watch for the masked rival; he is "
                    "the blueprint for nearly every anime antagonist you already "
                    "love."),
    say("NARRATOR", "That is your primer. Now go ride in knowing where the road "
                    "began."),
    {"t": "fx", "name": "sting", "gain": 0.6, "pre": 0.2},
]

DBZ_RECAP = [
    {"t": "fx", "name": "theme", "gain": 0.7},
    say("HOST_A", "Frame Theory is back. I am Delray, and after a week in the "
                  "quiet grief of classic Gundam, we are doing the loudest show "
                  "in the entire genre. Dragon Ball Z."),
    say("HOST_B", "Pip here, and let me give you the elevator pitch the way an "
                  "honest person would: this is the show that taught a whole "
                  "generation that any problem can be solved by screaming until "
                  "your hair changes color."),
    say("HOST_A", "And structurally it is brilliant and infuriating at the exact "
                  "same time. It is a treadmill of escalation. Every arc runs the "
                  "same loop: a new threat stronger than the last one, the heroes "
                  "train, they surpass it, repeat forever."),
    say("HOST_B", "But here is the part people get wrong. The engine of this "
                  "show is not the fighting. It is the waiting. The charge-up. "
                  "They monetized anticipation. A single punch can take three "
                  "episodes to actually land."),
    say("HOST_A", "And somehow it works, because the real stakes are emotional, "
                  "not tactical. The lead is not fighting to save the universe. "
                  "He is fighting because he wants a stronger opponent. He is a "
                  "cheerful zen monk with the temperament of a golden retriever."),
    say("HOST_B", "Meanwhile the prince — the proud rival — is the best-written "
                  "character in the whole thing and everyone quietly knows it. "
                  "Pride as a tragic flaw. A redemption arc you can measure in "
                  "how many years it takes him to admit that he cares about "
                  "anyone."),
    say("HOST_A", "We have to mention the power-level discourse. The show "
                  "introduces these precise numeric strength readings, and then "
                  "abandons them almost immediately, because numbers physically "
                  "cannot keep up with that much escalation. After a point it is "
                  "running on pure vibes."),
    say("HOST_B", "Fair criticisms: the filler, the recaps, and the way the "
                  "deep, capable female cast gets benched the second the real "
                  "punching starts."),
    say("HOST_A", "But the craft of a climax? Dragon Ball Z might be the best in "
                  "all of anime at the long, earned, hair-raising peak. The build "
                  "is endless, but the payoff genuinely lifts you out of your "
                  "seat."),
    say("HOST_B", "Which makes it the perfect audio commute show, weirdly. You "
                  "do not need a screen to know the hero is powering up. You can "
                  "hear it. The whole soundtrack is anticipation."),
    say("HOST_A", "Power levels and patience. That is the entire machine. Ride "
                  "safe, and we will see you next time on Frame Theory."),
    {"t": "fx", "name": "sting", "gain": 0.7, "pre": 0.2},
]

DBZ_PRIMER = [
    {"t": "fx", "name": "theme", "gain": 0.6},
    {"t": "fx", "name": "comm_open", "gain": 0.3},
    say("NARRATOR", "A Frame Theory primer, for the road. Spoiler-light — just "
                    "the shape of the thing, so you can ride in knowing what you "
                    "are hearing."),
    say("NARRATOR", "Dragon Ball Z is the loudest, most influential action "
                    "anime ever made. It follows a circle of martial artists who "
                    "defend their world from a rising ladder of ever-stronger "
                    "threats — aliens, androids, and worse."),
    say("NARRATOR", "Its rhythm is unique. Battles are enormous, drawn-out "
                    "events built around the charge — the slow, deliberate "
                    "gathering of power before release. The anticipation is the "
                    "point, and the sound design carries it."),
    say("NARRATOR", "Underneath the spectacle is a simple emotional core: a hero "
                    "who fights not for glory but for the joy of a worthy "
                    "opponent, and a rival whose entire arc is learning, very "
                    "slowly, how to care."),
    say("NARRATOR", "Because so much of its drama lives in voice, music, and the "
                    "rising hum before a clash, it travels beautifully without a "
                    "screen. You will always know when the moment is coming."),
    say("NARRATOR", "That is your primer. Eyes on the road, ears on the charge."),
    {"t": "fx", "name": "sting", "gain": 0.6, "pre": 0.2},
]


# =====================================================================
#  EPISODE CATALOG  (drives build + the site's episode switcher)
#
#  Each style's "build" is a directive the builder resolves:
#    "scene:pure" / "scene:described"  -> render SCENE in that style
#    "recap"                           -> render RECAP (with SCENE clips)
#    "track:NAME"                      -> render the named event list above
# =====================================================================

_PURE = ("Pure Cut", "Dialogue + sound design, zero narration.",
         "The scene, raw. Voices, comms, mecha, and ambient sound only — like "
         "watching with your eyes closed. Most immersive, asks the most of your "
         "imagination.")
_DESC = ("Audio-Described", "A narrator paints the action between the lines.",
         "Every visual beat is described by a narrator, the way audio "
         "description works for film. You will never lose the plot — ideal for a "
         "long ride where you can't glance at a screen.")
_RECAP = ("Recap Hosts", "Two hosts react and riff, with clips dropped in.",
          "A podcast ABOUT the episode — two hosts recap, joke, and pull in real "
          "clips. Lightest and most fun, least faithful. Great for catching up "
          "without committing.")
_COMMENT = ("Recap Hosts", "Two hosts dig into a classic — pure commentary.",
            "Delray and Pip break down a landmark series in their own words: "
            "history, craft, hot takes, and why it still matters. Transformative "
            "commentary — no clips, just analysis.")
_PRIMER = ("Audio Primer", "A narrator's spoiler-light field guide.",
           "A short, calm primer that sets up the series before you dive in — "
           "what it is, why it matters, what to listen for. Perfect for the "
           "first few minutes of a ride.")


def _style(sid, meta, build):
    label, tag, blurb = meta
    return {"id": sid, "label": label, "tag": tag, "blurb": blurb, "build": build}


EPISODES = [
    {
        "id": "exile-ep1",
        "series": "Mobile Frame: EXILE",
        "number": 1,
        "title": "Ashfall",
        "logline": EPISODE["logline"],
        "kind": "Original drama",
        "styles": [
            _style("pure", _PURE, "scene:pure"),
            _style("described", _DESC, "scene:described"),
            _style("recap", _RECAP, "recap"),
        ],
    },
    {
        "id": "frame-theory-gundam",
        "series": "Frame Theory",
        "number": 2,
        "title": "The Real Robot Revolution",
        "logline": "The hosts go back to 1979's Mobile Suit Gundam — the show "
                   "that turned giant robots from superheroes into equipment, and "
                   "rewrote the genre forever.",
        "kind": "Commentary",
        "styles": [
            _style("recap", _COMMENT, "track:GUNDAM_RECAP"),
            _style("primer", _PRIMER, "track:GUNDAM_PRIMER"),
        ],
    },
    {
        "id": "frame-theory-dbz",
        "series": "Frame Theory",
        "number": 3,
        "title": "Power Levels & Patience",
        "logline": "The loudest show in the genre, taken apart with love: why "
                   "Dragon Ball Z's treadmill of escalation works, and why it's "
                   "secretly the perfect thing to listen to.",
        "kind": "Commentary",
        "styles": [
            _style("recap", _COMMENT, "track:DBZ_RECAP"),
            _style("primer", _PRIMER, "track:DBZ_PRIMER"),
        ],
    },
]

# named event lists the builder can resolve from "track:NAME"
TRACKS = {
    "GUNDAM_RECAP": GUNDAM_RECAP,
    "GUNDAM_PRIMER": GUNDAM_PRIMER,
    "DBZ_RECAP": DBZ_RECAP,
    "DBZ_PRIMER": DBZ_PRIMER,
}
