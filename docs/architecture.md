# Architecture

Document de référence. À charger uniquement quand on touche à une frontière entre couches ou qu'on
introduit un nouveau composant. Le cadre quotidien est dans `CLAUDE.md`.

## 1. Vue d'ensemble

Deux chemins traversent la bibliothèque, et ils partagent le même modèle objet et le même écrivain :

```
HTML ─▶ parsing ─▶ cascade CSS ─▶ arbre de boîtes ─▶ layout ─▶ pagination ─┐
                                                                           ├─▶ modèle objet PDF ─▶ writer ─▶ octets
PDF existant ─▶ lexer ─▶ xref ─▶ objets paresseux ─▶ manipulation ────────┘
```

Le point de rencontre est volontaire : « ajouter une page HTML dans un PDF existant » n'est pas un cas
particulier, c'est la composition normale des deux chemins.

## 2. Découpage en packages

| Package | Rôle | Dépendances |
|---|---|---|
| `AdCodicem.Pdf` | Modèle objet, lecteur, écrivain, pages, polices, structure logique, sécurité, diagnostics | **aucune** |
| `AdCodicem.Pdf.Html` | Parsing HTML, moteur CSS, layout, peinture vers PDF | AngleSharp, HarfBuzzSharp, SkiaSharp |
| `AdCodicem.Pdf.AspNetCore` | Enregistrement DI, `IResult`, intégration MVC | `AdCodicem.Pdf.Html` |
| `AdCodicem.Pdf.Validation` | Validateur PDF/A et PDF/UA | `AdCodicem.Pdf` |
| `AdCodicem.Pdf.FacturX` | Factur-X / ZUGFeRD : embarquement, extraction, validation | `AdCodicem.Pdf` |
| `AdCodicem.Pdf.Rendering` | Rastérisation PDF → image (satellite tardif) | SkiaSharp |
| `AdCodicem.Pdf.Signing` | PAdES (satellite tardif) | `AdCodicem.Pdf` |

Règle d'or : **le cœur ne dépend de rien**. C'est ce qui garantit sa compatibilité Native AOT, son
empreinte mémoire et sa réutilisabilité côté serveur comme côté fonction serverless. Toute tentation
d'y faire entrer Skia, AngleSharp ou une dépendance native est un signal de mauvais découpage.

## 3. Le cœur `AdCodicem.Pdf`

```
Objects/      Modèle objet COS : PdfName, PdfNumber, PdfString, PdfArray, PdfDictionary,
              PdfStream, PdfReference. Immuable là où c'est possible, interné pour les noms.
IO/           PdfLexer (tokens), PdfParser (objets), Filters/ (Flate + prédicteurs, LZW, ASCII85,
              ASCIIHex, RunLength, passthrough DCT/JPX), XRef/ (table classique, flux xref,
              flux d'objets, chaîne /Prev, reconstruction), PdfWriter (écriture en flux).
Documents/    PdfDocument (ouverture, sauvegarde), PdfPage, PdfPageCollection, héritage
              d'attributs, copie profonde inter-documents, assemblage.
Fonts/        Parsing TrueType/OpenType, métriques, sous-ensemblage, embarquement Type0/CIDFontType2,
              CMap ToUnicode, registre et résolution de familles.
Content/      Écriture d'opérateurs de flux de contenu ; interpréteur de flux (extraction, M7).
Structure/    Arbre de structure logique (tagged PDF), contenu marqué, arbre des parents.
Security/     Déchiffrement et chiffrement RC4/AES, permissions.
Diagnostics/  PdfDiagnostics : anomalies, réparations, pertes de conformité.
```

### 3.1 Chemin de lecture

1. **Indexation** — on lit la fin du fichier (`startxref`), on suit la chaîne `/Prev` et on construit une
   table `numéro d'objet → (offset, génération)` ou `→ (flux d'objets, index)`. Seule cette table réside
   en mémoire : quelques dizaines d'octets par objet, indépendamment de la taille des objets.
2. **Réparation** — si `startxref` est faux, la table absente, ou un offset ne pointe pas sur l'objet
   attendu, on bascule sur un balayage complet du fichier à la recherche des motifs `N G obj`, en
   conservant la dernière définition de chaque objet. Chaque réparation est consignée dans le diagnostic.
3. **Résolution paresseuse** — `PdfReference.Resolve()` lit et parse l'objet à la demande. Un cache LRU
   borné (configurable, par défaut quelques milliers d'objets) évite de reparser les objets chauds
   (arbre des pages, ressources partagées) sans jamais retenir tout le document.
4. **Flux** — les données d'un flux ne sont ni lues ni décodées tant que l'appelant ne les demande pas.
   Un flux copié d'un document à l'autre transite **encodé**, sans décompression ni recompression.

### 3.2 Chemin d'écriture

Le writer écrit en avançant, sans jamais revenir en arrière : il est le seul à connaître les offsets.

- Les numéros d'objets sont **réservés à l'avance** et les corps écrits plus tard, ce qui permet de
  référencer un objet pas encore produit (arbre des pages, ressources partagées).
- Les flux sont écrits avec un `/Length` en **référence indirecte** : on compresse à la volée vers la
  sortie, puis on écrit l'objet longueur juste après. Aucun flux de contenu n'est bufferisé en entier.
- Deux modes de sauvegarde : **réécriture complète** (fichier compact, objets réordonnés, doublons
  éliminés) ou **mise à jour incrémentale** (ajout en fin de fichier, octets d'origine intacts —
  obligatoire pour ne pas invalider une signature existante).
- La sortie est déterministe : ordre d'écriture stable, `/ID` dérivé du contenu ou fourni par l'appelant.

### 3.3 Polices

Le PDF n'embarque pas des caractères mais des **glyphes**. La chaîne est donc :
texte → shaping (HarfBuzz, côté `.Html`) → identifiants de glyphes → encodage Identity-H → sous-ensemble
de la police embarqué. Le cœur ne fait pas de shaping : il reçoit des glyphes déjà résolus et se charge
des métriques, du sous-ensemblage et de l'embarquement, plus de la table `ToUnicode` sans laquelle le
texte n'est ni copiable ni accessible.

## 4. Le moteur HTML `AdCodicem.Pdf.Html`

```
Parsing/   AngleSharp : DOM conforme HTML5. Rien d'autre n'est utilisé d'AngleSharp.
Css/       Tokenizer CSS niveau 3, parseur de sélecteurs, feuille de style par défaut, cascade,
           héritage, valeurs calculées **typées** (structs, pas de chaînes).
Layout/    Arbre de boîtes, layout de bloc, layout en ligne (césure, alignement), tables, flex, grid,
           pagination (@page, sauts, en-têtes/pieds, compteurs).
Rendering/ Peinture de l'arbre de boîtes vers les opérateurs de contenu, liens, signets,
           émission de la structure logique balisée.
Fonts/     Résolution des familles CSS, @font-face, cache de polices, shaping HarfBuzz.
```

**Pourquoi un moteur CSS maison plutôt qu'AngleSharp.Css** : la valeur calculée d'AngleSharp.Css est une
chaîne qu'il faut reparser à chaque accès, ce qui est rédhibitoire dans une boucle de layout ; le paquet
est par ailleurs en préversion permanente. Le parsing HTML5, lui, est un travail ingrat, normatif et
parfaitement résolu par AngleSharp : on le réutilise sans hésiter.

**Séparation layout / peinture** : le layout ne connaît pas le PDF, la peinture ne recalcule rien. Cette
frontière est ce qui rendra possibles, plus tard, un backend de rastérisation ou un export SVG.

**Traçabilité DOM → PDF** : chaque boîte conserve une référence vers l'élément source. C'est la condition
pour émettre la structure logique (PDF/UA) et pour situer une erreur dans le HTML d'origine. Cette
information ne doit jamais être perdue par une couche intermédiaire.

## 5. Stratégie mémoire et CPU

- **Budget mémoire** : la consommation doit suivre la complexité de la **page** en cours, jamais la taille
  du document. Un rapport de 10 000 pages doit se générer dans la même empreinte qu'un rapport de 10.
- **Pooling** : tampons d'écriture, tableaux de glyphes, boîtes de layout passent par `ArrayPool<T>` ou des
  pools dédiés. Ce qui est loué est rendu, y compris en cas d'exception.
- **Structs et spans** : les valeurs CSS calculées, les métriques, les rectangles et les positions sont des
  structs. Le parsing travaille sur `ReadOnlySpan<byte>` sans matérialiser de chaînes.
- **Chaînes** : les noms PDF sont internés une fois ; le reste du parsing évite `string` autant que possible.
- **Asynchronisme** : l'API publique est asynchrone en entrée/sortie ; le calcul (layout, écriture) reste
  synchrone, car le paralléliser par document apporte plus que de le rendre asynchrone.
- **Parallélisme** : jamais implicite. Un document se génère sur un thread ; c'est l'appelant qui traite
  plusieurs documents en parallèle, et l'API doit rendre cela sûr et naturel.

## 6. Conformité

La conformité n'est pas une case à cocher en fin de chaîne, c'est une contrainte qui remonte jusqu'au
layout :

- **PDF/A** impose l'embarquement de toutes les polices, un profil ICC de sortie, des métadonnées XMP
  cohérentes avec le dictionnaire d'informations, et l'absence de certaines constructions.
- **PDF/UA** impose un arbre de structure logique complet, un ordre de lecture explicite, des textes de
  remplacement, une langue déclarée. D'où la traçabilité DOM → boîte → contenu marqué, qui doit exister
  dès le premier jour même si l'émission complète arrive plus tard.
- **En manipulation**, fusionner deux documents conformes doit produire un document conforme : recombinaison
  des arbres de structure, des `OutputIntents`, des métadonnées, dédoublonnage des polices. Ce qui ne peut
  pas être préservé est signalé dans le diagnostic.

## 7. Erreurs et diagnostics

| Situation | Réponse |
|---|---|
| Le fichier n'est pas un PDF, ou est illisible même après réparation | Exception `PdfException` |
| L'appelant demande l'impossible (page inexistante, mot de passe faux) | Exception typée |
| Le fichier est imparfait mais exploitable | Entrée dans `PdfDiagnostics`, traitement poursuivi |
| Une fonctionnalité CSS n'est pas supportée | Entrée dans le diagnostic, rendu dégradé, jamais d'échec |
| Une conformité est rompue par une opération | Entrée dans le diagnostic, avec la cause précise |

Le diagnostic est un objet retourné, pas un effet de bord : il s'inspecte, se sérialise et se teste.

## 8. Tests

- **Unitaires** : chaque composant, avec ses cas dégénérés. Le lexer et le parseur sont testés sur des
  entrées malformées autant que sur des entrées valides.
- **Round-trip** : ouvrir → sauvegarder → rouvrir → comparer sémantiquement. Invariant central du socle.
- **Corpus** : un jeu de PDF réels et volontairement cassés, versionné, avec le comportement attendu.
- **Empreintes** : les documents générés sont comparés octet à octet à une référence, ce qui n'est possible
  que grâce au déterminisme. Une différence intentionnelle se valide en régénérant la référence.
- **Visuel** : comparaison d'images rendues par un outil externe en CI, tant que la rastérisation n'est pas
  dans le périmètre.
- **Benchmarks** : BenchmarkDotNet, avec `MemoryDiagnoser` systématique. Une régression d'allocation est
  une régression.
- **Sécurité** : fuzzing du lexer et du parseur sur le corpus cassé ; aucune entrée ne doit provoquer
  d'exception non typée, de récursion infinie ni d'allocation démesurée.

## 9. Compatibilité et versionnement

SemVer strict. Tant que la version majeure est 0, l'API peut bouger, mais chaque rupture est consignée.
Un test d'API publique (fichier de référence des signatures exportées) rend toute rupture visible en
revue plutôt qu'après publication.
