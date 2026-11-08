Reprends le lot 19 du jalon M02 (étape 4) d'AdCodicem.Pdf là où la session cloud l'a laissé le 2026-11-08.

1. Récupère les deux branches et sépare les notes du code :
   - `git fetch origin ccr-6d6c0ee6-dqm4lm-188 ccr-6d6c0ee6-dqm4lm-188-wip` ;
   - `git switch ccr-6d6c0ee6-dqm4lm-188` : c'est la branche du lot, à da2b06f, propre et poussée, sans PR ;
   - `git worktree add ..\AdCodicem.Pdf-notes origin/ccr-6d6c0ee6-dqm4lm-188-wip` : les notes sont alors dans `..\AdCodicem.Pdf-notes\notes\batch19`.
2. Lis dans cet ordre, avant toute action :
   - `RESUME.md` : l'état exact, ce qui reste et les règles ;
   - `decisions.md` en entier, surtout la section « Review round 9 (wf_a01bab38-4ae on da2b06f): triage » et les réponses R9-Q1 et R9-Q2 à la fin ;
   - `lot19-r9-fixes.js` : le cahier des charges de chaque étape de correction.
3. Le round 9 de revue est fait et trié, et j'ai répondu à ses deux questions : S est reconnu par ses données, et l'avertissement ne nomme que les en-têtes typés sur leur ligne. Ses corrections restent toutes à faire : le workflow a été arrêté avant toute modification.
4. Commence par les corrections du round 9, dans l'ordre R9a, R9b, R9c, R9d, puis le suivi GitHub et la vérification. Pousse sur `ccr-6d6c0ee6-dqm4lm-188` après chaque étape.
5. Enchaîne ensuite : rounds de revue adverse jusqu'à un round sans constat bloquant, majeur ni mineur, étape ADR 53 sur tout le lot, vérification complète, `docs/status.md`, commentaires de fin de lot, puis PR `fix!:` avec « Closes … ».
6. Les règles permanentes sont dans `RESUME.md`, section 3 :
   - réponds-moi en français ; code, docs et commits en anglais américain ;
   - pose tes questions par AskUserQuestion, l'option recommandée en premier ;
   - aucune clé de revue dans src/, tests/ ou docs/ ;
   - un seul processus `dotnet` de build ou de test à la fois ;
   - ne réécris jamais l'historique poussé ; ne fusionne jamais la PR.
7. Sous Windows : adapte les chemins codés en dur (le dossier scratchpad du cloud) dans les scripts avant de les lancer, comme l'indique `RESUME.md`, section 5. Les scripts bash demandent Git Bash ou WSL, et les tests d'intégration demandent Docker Desktop.
