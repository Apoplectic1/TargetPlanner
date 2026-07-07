# Domain — observing context

The human/strategy home: the observing world TargetPlanner serves, distinct from [README.md](README.md) (app behaviour/UX) and [ARCHITECTURE.md](ARCHITECTURE.md) (code mechanics). Read when a change's "is this right?" depends on how the user actually images, not on the code.

Currently thin — populate as domain decisions land, rather than letting them scatter into chat history. What belongs here:

- **Sites** — the NamedSites (Penns Park is the seed boot default), their `.hrz` horizon profiles (produced by the external `HRZ Generator` tool, consumed by TP), elevation handling per site.
- **Capture workflow** — imaging runs on the BIRDWATCHER PC via NINA + the Target Scheduler (TS) plugin; TP plans (TPP = today's single-filter planning; TPS multi-filter scheduling mode is planned); XFM grades post-night. Cross-repo portfolio map lives in [`..\CLAUDE.md`](../CLAUDE.md).
- **Planning strategy** — filter choice vs the moon (Lorentzian avoidance gate, K-S sky brightness), target floor / min-duration preferences, what a "best session" means to the user in practice.
