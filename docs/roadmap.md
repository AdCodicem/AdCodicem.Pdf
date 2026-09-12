# Feuille de route

Index des jalons. Sert à situer un travail dans l'ensemble — **pas** à travailler au quotidien : la
spécification détaillée du jalon en cours vit dans `docs/milestones/`, et l'état réel dans `docs/status.md`.

Un jalon n'est pas une session : les gros jalons s'étalent sur plusieurs sessions, et un jalon n'est clos
que lorsque ses critères de sortie sont **vérifiés par des tests**, pas lorsque le code existe.

Taille indicative : **S** ≈ une session, **M** ≈ deux à trois, **L** ≈ une poignée, **XL** ≈ un chantier
à découper en sous-jalons.

| # | Jalon | Taille | Dépend de | État |
|---|-------|--------|-----------|------|
| M0 | Fondations du dépôt | S | — | en cours |
| M1 | Modèle objet et lecture tolérante | L | M0 | à faire |
| M2 | Écriture et fidélité de round-trip | M | M1 | à faire |
| M3 | Pages et assemblage de dossiers | M | M2 | à faire |
| M4 | Polices, texte et flux de contenu | L | M2 | à faire |
| M5 | Moteur HTML → PDF | XL | M4 | à faire |
| M6 | Structure balisée et accessibilité | M | M5 | à faire |
| M7 | Contenu sur documents existants | M | M3, M4 | à faire |
| M8 | Extraction et analyse | L | M3 | à faire |
| M9 | Sécurité et formulaires | L | M2 | à faire |
| M10 | Conformité PDF/A-3, Factur-X, validateur | L | M6, M8 | à faire |
| M11 | Optimisation, performance, durcissement | M | M5, M8 | à faire |
| M12 | Satellites : rastérisation, signature | XL | M8, M9 | à faire |

---

## M0 — Fondations du dépôt

**Objectif** : que n'importe quelle session puisse compiler, tester et publier sans rien découvrir.
**Livrables** : solution et découpage en projets, gestion centralisée des versions de paquets, CI GitHub
Actions (build, tests, benchmarks à la demande, publication sur tag), hook `SessionStart` installant le
SDK, cadre documentaire (`CLAUDE.md`, `architecture.md`, `decisions.md`, `roadmap.md`, `status.md`).
**Critères de sortie** : `dotnet test` vert en local et en CI ; un paquet NuGet se construit.

## M1 — Modèle objet et lecture tolérante

**Objectif** : ouvrir n'importe quel PDF, y compris imparfait, sans le charger en mémoire.
**Livrables** : modèle objet COS ; lexer et parseur travaillant sur `ReadOnlySpan<byte>` ; filtres Flate
(avec prédicteurs PNG et TIFF), ASCIIHex, ASCII85, RunLength, LZW ; table xref classique, flux xref, flux
d'objets, chaîne `/Prev`, fichiers à référence hybride ; résolution paresseuse avec cache borné ;
reconstruction par balayage quand la table est fausse ou absente ; `PdfDiagnostics`.
**Critères de sortie** : le corpus de tests s'ouvre intégralement, y compris les fichiers volontairement
cassés, avec les diagnostics attendus ; aucune entrée malformée ne provoque d'exception non typée, de
récursion infinie ou d'allocation démesurée ; l'empreinte mémoire d'ouverture d'un gros fichier reste
proportionnelle au nombre d'objets, pas à leur taille.

## M2 — Écriture et fidélité de round-trip

**Objectif** : réécrire ce qu'on a lu, à l'octet près sur le plan sémantique.
**Livrables** : `PdfWriter` en flux (numéros réservés d'avance, `/Length` indirect, compression à la volée) ;
table xref classique et flux xref ; flux d'objets en écriture ; sauvegarde complète et mise à jour
incrémentale ; `/ID` déterministe ; préservation d'une signature existante en mode incrémental.
**Critères de sortie** : ouvrir → sauvegarder → rouvrir donne un document sémantiquement identique sur tout
le corpus ; la mise à jour incrémentale laisse les octets d'origine intacts ; deux exécutions produisent
des octets identiques.

## M3 — Pages et assemblage de dossiers

**Objectif** : la priorité métier n°1 — composer un dossier à partir de pages générées et de PDF tiers.
**Livrables** : arbre des pages avec héritage d'attributs ; `PdfPageCollection` (insertion, suppression,
réordonnancement, rotation, extraction) ; copie profonde inter-documents avec dédoublonnage des ressources ;
fusion préservant signets, liens, annotations et pièces jointes ; API d'assemblage de haut niveau.
**Critères de sortie** : fusionner N documents produit un fichier valide dont la taille n'explose pas
(ressources dédoublonnées) ; les signets et liens internes pointent toujours au bon endroit après fusion ;
la mémoire consommée ne suit pas la taille cumulée des entrées.

## M4 — Polices, texte et flux de contenu

**Objectif** : écrire du texte correct, embarqué, extractible et accessible.
**Livrables** : parseur TrueType/OpenType (métriques, `cmap`, `hmtx`, `glyf`/`loca`, `CFF`) ;
sous-ensemblage ; embarquement Type0/CIDFontType2 et `ToUnicode` ; registre de polices et résolution de
familles ; écriture d'opérateurs de flux de contenu ; jeu de polices OFL embarqué.
**Critères de sortie** : un PDF généré affiche correctement du texte français accentué, se copie-colle
correctement, et ses polices sont embarquées sous-ensemblées ; les métriques correspondent à celles d'un
moteur de référence à une tolérance près.

## M5 — Moteur HTML → PDF

**Objectif** : le cœur de la promesse initiale. **XL — à découper en sous-jalons :**

- **M5.1** — Moteur CSS : tokenizer, sélecteurs, cascade, héritage, valeurs calculées typées, feuille par défaut.
- **M5.2** — Layout de bloc et en ligne, césure, alignements, pagination `@page`, marges, en-têtes et pieds, compteurs de pages.
- **M5.3** — Tables (modèle de largeur automatique et fixe, fusion de cellules, répétition d'en-tête).
- **M5.4** — Flexbox et grid simple.
- **M5.5** — Images (JPEG en passthrough, PNG, transparence), SVG en vectoriel, bordures et fonds.
- **M5.6** — Liens, signets, sommaire avec numéros de page réels, `@font-face`, API publique et intégration DI.

**Critères de sortie** : un jeu de documents de référence (facture, rapport multi-pages, contrat) est rendu
conformément aux images de référence ; la génération d'un document de 1 000 pages tient dans une empreinte
mémoire constante.

## M6 — Structure balisée et accessibilité

**Objectif** : produire des PDF réellement accessibles, pas seulement étiquetés comme tels.
**Livrables** : arbre de structure logique complet, contenu marqué et arbre des parents, textes de
remplacement, langue, ordre de lecture, artefacts pour les éléments décoratifs, tables balisées.
**Critères de sortie** : les documents de référence passent la validation PDF/UA d'un outil externe ; un
lecteur d'écran restitue un ordre de lecture correct.

## M7 — Contenu sur documents existants

**Objectif** : intervenir sur un PDF reçu sans le régénérer.
**Livrables** : filigranes et tampons (texte ou fragment HTML rendu), numérotation, superposition et
sous-position, en-têtes et pieds ajoutés après coup, N-up et imposition, fusion de dictionnaires de
ressources sans collision de noms.
**Critères de sortie** : tamponner un document ne modifie ni son contenu d'origine ni sa conformité, et le
diagnostic signale toute perte.

## M8 — Extraction et analyse

**Objectif** : lire ce que contient un PDF.
**Livrables** : interpréteur de flux de contenu (états graphiques, texte, positions) ; glyphes positionnés
avec police et taille ; regroupement en mots, lignes, blocs, colonnes ; détection de tableaux avec indice
de confiance ; lecture prioritaire de la structure balisée quand elle existe ; extraction d'images, de
métadonnées, de signets et de pièces jointes.
**Critères de sortie** : le texte extrait d'un corpus de référence correspond à l'attendu ; l'ordre de
lecture est correct sur les documents à colonnes ; l'extraction d'un gros document reste en mémoire bornée.

## M9 — Sécurité et formulaires

**Objectif** : ouvrir les documents protégés, produire des documents protégés, traiter les formulaires.
**Livrables** : déchiffrement RC4 40/128 et AES-128/256, chiffrement et permissions ; AcroForms — lecture,
remplissage, aplatissement, apparence des champs.
**Critères de sortie** : les documents chiffrés du corpus s'ouvrent ; un formulaire rempli s'affiche
correctement dans les lecteurs courants, avant comme après aplatissement.

## M10 — Conformité PDF/A-3, Factur-X et validateur

**Objectif** : la conformité réglementaire, garantie et vérifiable.
**Livrables** : génération PDF/A-2b et PDF/A-3b (profil ICC, XMP, contraintes de rendu) ; embarquement et
extraction Factur-X/ZUGFeRD ; préservation active de la conformité lors des fusions ; validateur intégré
PDF/A et PDF/UA, livré par paliers.
**Critères de sortie** : les documents produits passent veraPDF ; le validateur intégré est cohérent avec
lui sur le corpus.

## M11 — Optimisation, performance et durcissement

**Objectif** : tenir la promesse de sobriété, chiffres à l'appui.
**Livrables** : dédoublonnage global des ressources, recompression, sous-ensemblage des polices héritées,
linéarisation ; campagne de benchmarks et budgets de performance en CI ; validation Native AOT et trimming ;
fuzzing du lexer et du parseur.
**Critères de sortie** : budgets de débit et d'allocation définis et tenus en CI ; une régression
d'allocation fait échouer la CI ; un exécutable AOT génère un document.

## M12 — Satellites : rastérisation, signature

**Objectif** : les extensions qui supposent tout le reste en place.
**Livrables** : `AdCodicem.Pdf.Rendering` (rastérisation via Skia, réutilisant l'interpréteur de M8) ;
`AdCodicem.Pdf.Signing` (abstraction `IPdfSigner`, implémentation locale, horodatage, chemin vers HSM).
**Critères de sortie** : les vignettes produites correspondent aux images de référence ; une signature
produite est validée par un lecteur de référence.

---

## Travailler un jalon

1. Lire `CLAUDE.md`, `docs/status.md`, puis `docs/milestones/<jalon>.md`.
2. Travailler par tranche verticale testable, jamais par couche horizontale entière.
3. Une fonctionnalité livrée = code + tests + entrée dans le journal de `status.md`.
4. Ce qui est découvert en route et sort du jalon va dans la dette de `status.md`, pas dans le code.
5. Clore un jalon = ses critères de sortie sont vérifiés par des tests, et `status.md` le reflète.

## Ajouter un jalon

Créer `docs/milestones/<numéro>.md` à partir de `docs/milestones/_template.md`, ajouter la ligne dans le
tableau ci-dessus, et n'y détailler que ce qui est décidé — un jalon lointain reste volontairement grossier.
