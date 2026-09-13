# État du projet

Fichier vivant, mis à jour **à la fin de chaque session**. Il décrit l'état réel, pas les intentions.
Le garder court : le journal se résume dès qu'il dépasse une dizaine d'entrées — l'historique détaillé
est dans git, pas ici.

## En un coup d'œil

- **Jalon en cours** : M1 — Modèle objet et lecture tolérante (`docs/milestones/M1.md`), tranches 1 à 7 écrites
- **Dernier jalon clos** : M0 — Fondations du dépôt
- **Compile** : oui — **Tests** : 73, tous verts — **CI** : à valider au premier push
- **Branche** : `claude/nuget-pdf-html-dotnet-msyz8z`

### Mesures actuelles (BenchmarkDotNet, ShortRun)

| Opération | Document | Temps | Alloué |
|---|---|---|---|
| Indexation | 1 000 pages, ~4 Mo | 229 µs | 393 Ko |
| Indexation + lecture de toutes les pages | 1 000 pages, ~4 Mo | 6,2 ms | 5,9 Mo |

L'écart entre les deux lignes est la promesse de la bibliothèque : ouvrir un document ne lit pas son
contenu. L'indexation coûte environ 200 octets par objet, indépendamment du poids des objets.

## Prochain pas concret

Clore M1 : constituer un corpus de PDF réels (T03), poser un budget mémoire vérifié en CI, et fuzzer le
lexer et le parseur. Ensuite M2 — l'écriture et la fidélité de round-trip.

## Journal

### 2026-09-13 — M1, tranches 1 à 7
- Modèle objet COS, lexer et parseur tolérants, filtres de décodage, lecture des quatre formes d'index
  (table classique, flux xref, flux d'objets, chaîne `/Prev`, fichiers hybrides), résolution paresseuse
  avec cache borné, réparation par balayage, diagnostics structurés.
- Durcissement : toute allocation dictée par une valeur du fichier est bornée (longueur de flux ramenée à
  la taille réelle du fichier, nombre d'objets d'un flux d'objets ramené à ce que son en-tête peut
  contenir), et les cycles — références, `/Prev`, flux d'objets se contenant lui-même — se terminent.
- Un fichier sans aucun objet exploitable est refusé par une exception typée plutôt qu'ouvert à vide.
- 73 tests, dont une classe entière dédiée aux entrées hostiles, avec budget de temps par test.
- Les tests fabriquent leurs PDF octet par octet avec des offsets exacts, puis les cassent volontairement :
  pas de dépendance réseau ni de fichiers binaires dans le dépôt.
- Outillage : le SDK .NET 10 s'installe depuis l'archive Ubuntu ; `dotnet test` exige désormais
  Microsoft.Testing.Platform (opt-in dans `global.json`) et la syntaxe `--solution`.

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
| T06 | `PdfString.ToText` lit le Latin-1 au lieu du PDFDocEncoding complet (les 32 positions 0x80-0x9F diffèrent) | Avant la première version publique |
| T07 | Le cache d'objets évince en FIFO et non en LRU ; les noms sont internés via une chaîne intermédiaire | M11, mesure à l'appui |
| T08 | Fuzzing du lexer et du parseur non mis en place | Clôture de M1 |
| T09 | Aucun budget mémoire vérifié en CI | Clôture de M1 |
