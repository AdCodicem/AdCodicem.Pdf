# AdCodicem.Pdf — cadre de travail

Bibliothèque **.NET 10 / C# 14** publiée en NuGet (MIT) qui **génère des PDF à partir de HTML** et
**manipule des PDF existants**, avec une exigence permanente de sobriété CPU et mémoire.

## Comment aborder une session

Lis dans cet ordre, et rien de plus :

1. **ce fichier** — le cadre invariant ;
2. **`docs/status.md`** — où en est le projet, ce qui est en cours, la dette connue ;
3. **le fichier du jalon en cours** dans `docs/milestones/` — la spécification détaillée du travail.

Ne charge `docs/architecture.md` que si tu touches à une frontière entre couches, et
`docs/decisions.md` que si tu envisages de remettre en cause un choix déjà acté.
`docs/roadmap.md` sert à situer un jalon dans l'ensemble, pas à travailler au quotidien.

À la fin de chaque session : mets `docs/status.md` à jour (état réel, pas intentions), coche la
checklist du jalon, commite, pousse.

## Ce que la bibliothèque est — et n'est pas

- Un moteur HTML → PDF **entièrement managé** : pas de Chromium, pas de wkhtmltopdf, pas de process externe.
- Un couple **lecteur/écrivain PDF** capable d'ouvrir des fichiers tiers imparfaits sans les charger en mémoire.
- Elle ne vise **pas** la fidélité d'un navigateur : pas de JavaScript, pas d'animations, pas de rendu
  de pages web arbitraires. Elle vise les **documents métier** — factures, rapports, contrats, dossiers.

## Décisions actées — ne pas les re-litiger

| # | Décision |
|---|----------|
| D01 | Moteur de rendu 100 % managé : AngleSharp (parsing HTML5) → CSS et layout maison → writer PDF maison |
| D02 | Périmètre complet : génération **et** manipulation (assemblage, contenu, extraction, formulaires, sécurité, optimisation) |
| D03 | Contenu cible : documents métier + sous-ensemble CSS moderne choisi (flex, grid simple, SVG, paged media) |
| D04 | SkiaSharp et HarfBuzzSharp autorisés — dans `AdCodicem.Pdf.Html` uniquement, jamais dans le cœur |
| D05 | Le writer PDF est écrit par nous : contrôle total sur compression, conformité, streaming |
| D06 | Structurant dès la conception : en-têtes/pieds/numérotation/liens/signets, PDF/A-3 + Factur-X, PDF/UA (tagged) |
| D07 | API : façade + options immuables + intégration DI ASP.NET Core |
| D08 | Découpage : cœur sans dépendance + packages satellites |
| D09 | Cible unique `net10.0`, C# 14 |
| D10 | Polices : registre explicite + jeu OFL embarqué + webfonts CSS téléchargeables (désactivé par défaut) |
| D11 | CI GitHub Actions, publication nuget.org |
| D12 | Lecture paresseuse ; sortie au choix en réécriture complète ou mise à jour incrémentale |
| D13 | Lecteur **tolérant** aux fichiers non conformes, avec rapport de diagnostic structuré |
| D14 | Extraction de texte complète : glyphes positionnés → lignes/paragraphes → tableaux, en privilégiant la structure balisée quand elle existe |
| D15 | Rastérisation PDF → image : package satellite, après le socle |
| D16 | Conformité préservée activement lors des manipulations, plus un validateur PDF/A et PDF/UA intégré |
| D17 | Signature : place réservée dans le writer (mise à jour incrémentale, préservation des signatures) ; PAdES plus tard |

Priorité métier n°1 après le socle : **l'assemblage de dossiers** (pages générées + PDF tiers, sommaire,
signets, pagination continue).

## Invariants d'architecture — non négociables

1. **Le cœur `AdCodicem.Pdf` n'a aucune dépendance**, ni NuGet ni native, et reste compatible Native AOT et trimming.
2. **Rien ne charge un document entier en mémoire.** Lecture paresseuse par objet, écriture en flux,
   consommation mémoire fonction de la page la plus lourde, pas de la taille du fichier.
3. **Pas d'allocation dans les boucles chaudes** — parsing, layout, écriture : `Span<T>`, `ArrayPool<T>`,
   buffers réutilisés. Pas de LINQ, pas de `string.Split`, pas de closure, pas de boxing sur ces chemins.
   Ailleurs, la lisibilité prime.
4. **Toute donnée lue d'un fichier tiers est hostile.** Aucune allocation dimensionnée par une valeur du
   fichier sans borne vérifiée, aucune récursion non bornée, aucune boucle dont la sortie dépend d'un
   offset lu. Un PDF malformé produit un diagnostic, jamais un plantage ni un déni de service.
5. **Les anomalies vont dans `PdfDiagnostics`**, pas dans un logger et pas dans une exception, tant que la
   lecture peut continuer. Les exceptions sont réservées à ce qui rend l'opération impossible.
6. **Déterminisme** : mêmes entrées → mêmes octets en sortie. Les seules sources de variation autorisées
   sont fournies explicitement par l'appelant (date de création, identifiant de document).
7. **Conformité** : on préserve PDF/A et la structure balisée, ou on signale explicitement la perte dans le
   rapport. Jamais de rupture silencieuse.
8. **Sûreté des types publics** : l'API publique est immuable par défaut, sans état statique mutable.
   Un `PdfDocument` n'est pas thread-safe ; un moteur de rendu l'est.
9. Toute fonctionnalité arrive **avec ses tests**. Toute optimisation arrive **avec son benchmark**.

## Environnement de développement

Sessions Claude Code sur le web : le SDK .NET n'est pas préinstallé et les serveurs de Microsoft sont
bloqués par la politique réseau. Le hook `SessionStart` (`.claude/scripts/setup-dotnet.sh`) installe
`dotnet-sdk-10.0` depuis l'archive Ubuntu. nuget.org est joignable, `dotnet restore` fonctionne.
Si `dotnet` est introuvable : `apt-get install -y --no-install-recommends dotnet-sdk-10.0`.

```bash
dotnet build   AdCodicem.Pdf.slnx -c Release
dotnet test    AdCodicem.Pdf.slnx -c Release
dotnet run -c Release --project bench/AdCodicem.Pdf.Benchmarks -- --filter '*'
```

## Conventions

- **Code, API publique et commentaires XML en anglais** ; documentation projet (`docs/`, `CLAUDE.md`)
  en français. Le `README.md` est la vitrine publique du paquet : en anglais.
- Un fichier par type public. `sealed` par défaut. `internal` tant qu'une API n'est pas décidée publique.
- Les types du modèle objet PDF portent le préfixe `Pdf` ; les types internes au moteur HTML n'en portent pas.
- Tests : xUnit + Shouldly. Un test nommé décrit un comportement, pas une méthode.
- Commits conventionnels (`feat:`, `fix:`, `perf:`, `docs:`, `test:`, `refactor:`, `build:`).
- Branche de développement : `claude/nuget-pdf-html-dotnet-msyz8z`.

## Pièges connus

- Le PDF est un format à **offsets absolus** : toute écriture qui décale des octets invalide la table xref.
  Seul `PdfWriter` connaît les positions ; aucune couche supérieure ne calcule d'offset.
- Les nombres réels PDF **n'admettent pas la notation exponentielle** : formater en `"0.####"` invariant.
- Une chaîne de texte PDF non ASCII doit être écrite en **UTF-16BE avec BOM** — sinon les accents français cassent.
- Un flux `/Length` peut être une **référence indirecte** ; c'est ce qui rend l'écriture en flux possible.
- Les attributs de page (`Resources`, `MediaBox`, `Rotate`) sont **hérités** dans l'arbre des pages : toujours
  passer par la résolution héritée, jamais lire le dictionnaire de page directement.
