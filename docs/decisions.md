# Décisions d'architecture

Une décision actée ne se rediscute pas sans élément nouveau. Ce fichier existe pour éviter de refaire
le même débat à chaque session : il donne le choix, la raison, et ce qui a été écarté.

Format : décision — raison — alternatives écartées — ce qui la remettrait en cause.

---

**D01 — Moteur de rendu 100 % managé.** AngleSharp pour le parsing HTML5, moteur CSS et layout écrits par
nous, writer PDF écrit par nous.
*Raison* : c'est la seule option compatible avec l'objectif de sobriété. Une instance Chromium consomme
150 à 300 Mo et démarre en centaines de millisecondes ; un moteur managé rend une facture en quelques
mégaoctets et quelques millisecondes, et se déploie en conteneur distroless.
*Écarté* : wrapper Chromium/Playwright (empreinte et complexité de déploiement), binding natif type PDFium
(dépendances par RID, incompatible AOT).
*Remise en cause* : un besoin avéré de rendre des pages web arbitraires avec JavaScript — auquel cas on
ajoute un backend satellite, sans toucher au cœur.

**D02 — Périmètre complet : génération et manipulation.** Décidé après un premier cadrage « génération
seule ».
*Conséquence majeure* : la manipulation impose un parseur complet et un modèle objet en lecture/écriture.
Ce n'est pas un module greffé sur le writer, c'est la moitié basse de la bibliothèque, et les deux
chemins partagent le même modèle objet.

**D03 — Documents métier avec sous-ensemble CSS moderne.** Box model complet, tables, paged media, flexbox,
grid simple, SVG inline. Pas de JavaScript.
*Raison* : couvre factures, rapports, contrats, étiquettes — la totalité des usages visés — pour une
fraction du coût d'un moteur web complet.

**D04 — SkiaSharp et HarfBuzzSharp autorisés, dans `.Html` uniquement.** HarfBuzz pour le shaping (ligatures,
crénage, écritures complexes, bidirectionnel), Skia pour le décodage d'images et le rendu SVG de secours.
*Raison* : réécrire un shaper de qualité représente des années ; le résultat serait typographiquement
inférieur. Le confinement au package HTML préserve un cœur sans dépendance.
*Écarté* : tout en managé (coût disproportionné), Skia partout (contamine le cœur).

**D05 — Writer PDF maison plutôt que le backend PDF de Skia.**
*Raison* : Skia produit un PDF opaque — pas de PDF/A, pas de structure balisée, pas de contrôle de la
compression, pas d'écriture en flux. Or la conformité et le streaming sont au cœur de la promesse.
*Écarté* : `SKDocument.CreatePdf` (rapide à livrer, impasse ensuite).

**D06 — Conformité structurante dès la conception.** En-têtes/pieds, numérotation, liens, signets ;
PDF/A-3 et Factur-X ; PDF/UA balisé.
*Raison* : la traçabilité DOM → boîte → contenu marqué qu'exige PDF/UA est impossible à rajouter après
coup sans réécrire le layout. Elle est donc présente dès le premier jalon, même si l'émission complète
de l'arbre de structure arrive plus tard.

**D07 — Façade + options immuables + DI.** `IPdfRenderer` singleton thread-safe, `record` d'options,
`AddAdCodicemPdf()`.
*Écarté* : builder fluent en API primaire (peut être ajouté au-dessus plus tard, l'inverse est faux).

**D08 — Cœur sans dépendance + satellites.** Voir le tableau des packages dans `architecture.md`.
*Raison* : un consommateur qui ne fait que manipuler des PDF ne doit pas tirer AngleSharp ni un binaire natif.

**D09 — `net10.0` uniquement, C# 14.**
*Raison* : accès sans compromis aux API de performance récentes, aucune compilation conditionnelle.
*Écarté* : multiciblage `net8.0` (double matrice, API perf indisponibles). À reconsidérer seulement si un
consommateur réel reste bloqué en LTS.

**D10 — Polices : registre explicite, jeu OFL embarqué, webfonts optionnelles.**
*Raison* : un conteneur n'a aucune police installée ; dépendre des polices système rend le rendu non
reproductible. Le jeu embarqué garantit que le premier essai fonctionne. Le téléchargement de `@font-face`
distants est possible mais **désactivé par défaut** : appel réseau pendant un rendu, donc non déterministe
et exposé côté sécurité.

**D11 — GitHub Actions, publication nuget.org.** Le dépôt est sur GitHub ; la CI est vérifiable depuis les
sessions de développement, ce qui n'est pas le cas d'Azure Pipelines.

**D12 — Lecture paresseuse ; sortie en réécriture complète ou incrémentale.**
*Raison* : c'est ce qui permet de manipuler un document de plusieurs centaines de mégaoctets dans quelques
mégaoctets de RAM, et de ne pas invalider une signature existante.
*Écarté* : chargement complet en mémoire (approche de la plupart des bibliothèques .NET, contraire à
l'objectif), pipeline en flux pur (interdit réordonnancement, dédoublonnage global, formulaires).

**D13 — Lecteur tolérant avec rapport de diagnostic.** Reconstruction de xref, récupération sur objets
malformés, plus un rapport structuré des anomalies et des réparations.
*Raison* : les PDF réels sont fréquemment non conformes ; un lecteur strict échoue là où tous les lecteurs
du marché réussissent. Le rapport permet de tracer la qualité des fichiers entrants.

**D14 — Extraction de texte complète, structure balisée prioritaire.** Glyphes positionnés, regroupement en
lignes et paragraphes, détection de tableaux, et lorsque le document est balisé on suit son arbre de
structure plutôt que les heuristiques.
*Note* : la détection de tableaux est heuristique par nature ; l'API doit exposer un indice de confiance
plutôt que laisser croire à un résultat exact.

**D15 — Rastérisation en package satellite, après le socle.** Elle réutilisera l'interpréteur de flux de
contenu écrit pour l'extraction. D'ici là, les tests visuels s'appuient sur un outil externe en CI.

**D16 — Conformité préservée activement, plus un validateur intégré.** À la fusion, on recombine réellement
les arbres de structure, les `OutputIntents`, les métadonnées et les polices.
*Réserve assumée* : un validateur PDF/A complet représente des centaines de règles. Il sera livré par
paliers, en couvrant d'abord ce que la bibliothèque produit elle-même, puis les documents tiers.

**D17 — Signature : place réservée.** Le writer doit savoir produire des mises à jour incrémentales et
préserver une signature existante intacte. PAdES viendra ensuite, derrière une abstraction `IPdfSigner`
permettant de déléguer à un HSM ou à un prestataire qualifié.

---

## Décisions techniques mineures mais durables

- Unité interne du layout : le **pixel CSS** ; conversion en points (`× 0.75`) au moment de la peinture seulement.
- Le repère PDF est en bas à gauche, le layout travaille en haut à gauche : la conversion se fait à un seul
  endroit, dans la peinture.
- Les noms PDF sont internés ; les entiers usuels sont mis en cache.
- Un flux copié d'un document à l'autre transite **encodé**, sans cycle décompression/recompression.
- Le `/ID` du document est dérivé du contenu, ou fourni par l'appelant, jamais aléatoire — le déterminisme
  prime, et un identifiant aléatoire rendrait tout test d'empreinte impossible.
