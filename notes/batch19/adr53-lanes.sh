#!/bin/sh
# Batch 19 ADR 53 run: 27 shards, 4 lanes of 2 workers, --stop-on-fail, at 19a2296.
export DOTNET_CLI_UI_LANGUAGE=en-US
cd "C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/adr53"
ls shards/shard-*.json | xargs -P 4 -I{} sh -c 'timeout 14400 python -I "C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/adr53/run.py" "{}" 19a2296 2 --stop-on-fail --repo C:/Users/clemm/repos/AdCodicem.Pdf-notes >> "C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/adr53/lane-$(basename {} .json).log" 2>&1; rc=$?; echo "$(date +%H:%M:%S) {} rc=$rc" >> "C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/adr53/lanes.log"'
echo "$(date +%H:%M:%S) all done" >> "C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/adr53/lanes.log"
