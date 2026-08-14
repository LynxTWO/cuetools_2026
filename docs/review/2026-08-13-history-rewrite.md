# History rewrite, 2026-08-13

The owner asked for the "Co-Authored-By: Claude" trailers to be removed from
all commit messages. Both repositories were rewritten in place on 2026-08-13
evening; every commit SHA from 2026-07-02 forward changed as a result. This
note is the receipt for that operation, because several documents in
`docs/` cite commits by hash and those citations were updated to the
rewritten values in the same change as this note.

What was done, in order:

- Linux PR #26-era work was merged first so master was quiescent.
- Both histories were rewritten with git-filter-repo v2.47.0 using a
  message-only callback that strips trailer lines beginning with
  `Co-Authored-By: Claude`. 265 fork commits and 47 cuetools-linux commits
  carried the trailer; zero remain.
- Verified before pushing: the fork's tree-hash sequence across all 1,232
  commits is byte-identical to the old history; the cuetools-linux rewrite
  additionally remapped its 82 `extern/cuetools_2026` submodule pins through
  the fork's old-to-new commit map, and every remapped pin resolves in the
  rewritten fork. Non-gitlink tree entries are unchanged.
- The three `preview-2026.1.0*` releases and tags were deleted at the
  owner's direction, because their evidence archives cite pre-rewrite
  hashes; a fresh unsigned preview will be issued from the rewritten
  history. The old release evidence remains internally self-consistent but
  can no longer be resolved against this repository.
- The `protect-master` rulesets were disabled for the force-push window and
  re-enabled immediately after; enforcement was confirmed active on both
  repositories.

Old pull-request pages on GitHub retain their original commits, so
pre-rewrite hashes cited by closed PRs still render there; they are no
longer reachable from any branch or tag.

Hash citations in `docs/` were mapped old-to-new using the filter-repo
commit map (17 distinct citations across 14 files). The underlying claims,
measurements, and file contents those citations vouch for are unchanged;
the trees at the cited commits are byte-identical by construction.
