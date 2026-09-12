# État du projet

Fichier vivant, mis à jour **à la fin de chaque session**. Il décrit l'état réel, pas les intentions.
Le garder court : le journal se résume dès qu'il dépasse une dizaine d'entrées — l'historique détaillé
est dans git, pas ici.

## En un coup d'œil

- **Jalon en cours** : M1 — Modèle objet et lecture tolérante (`docs/milestones/M1.md`)
- **Dernier jalon clos** : M0 — Fondations du dépôt
- **Compile** : oui — **Tests** : à créer — **CI** : à valider au premier push
- **Branche** : `claude/nuget-pdf-html-dotnet-msyz8z`

## Prochain pas concret

Tranche 1 de M1 : modèle objet COS (`Objects/`) et ses extensions d'accès typé, avec tests unitaires.

## Journal

### 2026-09-12 — Cadrage et fondations
- Périmètre arrêté en deux temps : d'abord génération HTML → PDF seule, puis extension à la manipulation
  complète de PDF existants. Dix-sept décisions consignées dans `docs/decisions.md`.
- Cadre documentaire posé : `CLAUDE.md` (cadre de session), `architecture.md`, `decisions.md`,
  `roadmap.md` (M0 à M12), `milestones/` (spécification par jalon), ce fichier.
- Squelette de solution : cœur, moteur HTML, intégration ASP.NET Core, tests, benchmarks ;
  cible `net10.0`, gestion centralisée des versions de paquets.
- Environnement : le SDK .NET n'est pas préinstallé dans les sessions web et les serveurs Microsoft sont
  bloqués par la politique réseau. Contourné par l'archive Ubuntu (`dotnet-sdk-10.0`), automatisé par le
  hook `SessionStart`.

## Dette et points ouverts

| # | Sujet | Décision attendue |
|---|-------|-------------------|
| T01 | `TreatWarningsAsErrors` est désactivé le temps que le socle se stabilise | À activer à la clôture de M2 |
| T02 | La documentation XML (`CS1591`) n'est pas exigée sur l'API publique | À exiger quand l'API publique se fige (M5.6) |
| T03 | Corpus de PDF de test à constituer (fichiers réels, fichiers cassés, fichiers hostiles) | Pendant M1, sans dépendance réseau |
| T04 | Jeu de polices OFL à embarquer pour le rendu par défaut | Pendant M4 |
| T05 | Test d'API publique (référence des signatures exportées) | À mettre en place au début de M5 |
