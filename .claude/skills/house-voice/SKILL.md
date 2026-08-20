---
name: house-voice
description: Use when writing or revising any human-facing prose in the CUETools 2026 repositories - manual pages, engineering docs, README, PR descriptions, reports to the owner. Warms the writing to read like a person without manufacturing errors. Levels 1-3; surface map in D-077 (cuetools-linux DECISION-LOG).
---

# House voice: warm, never damaged

Adapted from the owner's Internet Human Mode skill (2026-08-20). Its
core rule survives whole: **change the surface, never the thought.**
Its upper levels do not survive: this house never manufactures typos,
dropped apostrophes, or rushed-typing texture to look human. We write
most of this prose with an AI in the loop; injecting fake typing
errors would fabricate provenance, which is the exact arms race the
source skill's own demo post argues against. Humanity comes from
voice, not damage.

## The dial

Three levels, chosen per surface (the map is D-077 in the
cuetools-linux DECISION-LOG):

- **Level 1, warm precision.** Manual pages, glossary, engineering
  records (ARCHITECTURE, ENGINEERING, DECISION-LOG, slice briefs,
  findings). Contractions, direct "you", varied sentence length,
  plain verbs. Every fact rule still binds: receipts, statuses,
  verbatim UI strings, exact numbers. Warmth never spends a receipt.
- **Level 2, conversational.** README, release notes, pull-request
  descriptions, reports to the owner. Looser rhythm, honest humor
  where something is genuinely funny, the occasional aside. Still
  zero manufactured errors.
- **Level 3, personality.** Teaching surfaces only (the How a CD
  Works manual page is the standing example). Wonder and wry asides
  are welcome. The numbers stay exact; a joke that bends a fact is a
  fact error with a smile on it.

Commit messages are already governed (brief, human, no AI tells) and
stay as they are. Code comments are exempt: the file's idiom wins.

## What to warm

Formal transitions, rhetorical symmetry, template stiffness, repeated
grammatical perfection, passive constructions, nominalizations
("perform a verification" -> "verify"). Read the sentence aloud at
the bench; if it sounds like a deliverable, recast it.

Owner rulings from the first golden round (2026-08-20): asides go in
parentheses, never paired " - " dashes; when a " - " connector joins
two halves that could stand alone, prefer the sentence break (a plain
pivot like "Basically," is welcome); and name the thing - a count says
what it counts, a pronoun gets its noun back when the referent is more
than a line away.

## What never changes

- Facts, numbers, flags, paths, commands, commit hashes, UI strings.
- Receipts, statuses, qualifications, safety and consent language.
- The ASCII rules: no em dashes, no typographic Unicode, no emoji.
- No comic archetypes (the source skill's Chad/Stacy/GymBro cast
  stays on the internet where it lives).
- Searchability: never respell a word a reader might grep for.

## Failure modes to avoid

1. **Fake stupidity.** Never lower the vocabulary or flatten the
   reasoning to seem approachable. Complex idea, casual delivery.
2. **Scheduled warmth.** Not every sentence needs to smile. Contrast
   carries: a clean, plain line inside warm prose lands hardest.
3. **Puffed records.** A DECISION entry or acceptance criterion is a
   record first; warm its prose, never its precision.
4. **Humor that isn't true.** If the joke needs the fact to bend,
   cut the joke.
5. **Same-voice everything.** The lesson page may sound delighted;
   the recovery dialog must not. Match the surface's stakes.

## Quality check before returning prose

1. Did the thought survive at full strength?
2. Are all facts, numbers, and quoted strings byte-identical?
3. Would this read naturally spoken aloud?
4. Is there a single manufactured error anywhere? (Must be no.)
5. Does the warmth level match the surface's entry in D-077?
