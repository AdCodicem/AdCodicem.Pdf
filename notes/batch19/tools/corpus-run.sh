#!/usr/bin/env bash
# Corpus dump: main tree (21a0298) and branch tree alternated, two runs each.
S=/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r7f
B=/home/user/AdCodicem.Pdf/.claude/worktrees/wf_5cf097f0-910-6
M=$S/main-tree
: > $S/corpus-progress.txt
for run in 1 2; do
  for t in main branch; do
    if [ $t = main ]; then d=$M; else d=$B; fi
    cd $d || exit 1
    ZZ_CORPUS_OUT=$S/corpus-$t-$run.txt timeout 1800 dotnet test --project tests/AdCodicem.Pdf.Tests/AdCodicem.Pdf.Tests.csproj -c Release --no-build -- --filter-method "*ZzCorpusDumpAll" > $S/corpus-$t-$run.log 2>&1
    echo "$(date +%T) $t run $run exit=$?" >> $S/corpus-progress.txt
  done
done
echo done >> $S/corpus-progress.txt
