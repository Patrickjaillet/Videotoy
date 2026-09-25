# Roadmap — Corrections à apporter

Liste des problèmes identifiés lors d'une revue de code du dépôt (rendu Direct3D 11,
pipeline d'export FFmpeg, transpileur de shaders, application WPF). Classés par
catégorie et par sévérité. État après la passe de correction du
24/09/2026 (voir notes `>` sous chaque item corrigé).

## Critique

- [x] **FFmpeg : fuite de `Process` si `Process.Start()` échoue.** Dans
  `src/Videotoy.Ffmpeg/FfmpegService.cs` (`LaunchProcessAsync`, ~L237-266), si
  `Start()` lève une exception (ex. `ffmpeg.exe` manquant ou bloqué), `_process`
  reste non-null mais n'a jamais démarré. `IsRunning` (L40) appelle alors
  `HasExited` sur un process non démarré, ce qui lève `InvalidOperationException`
  et bloque définitivement le service (tous les exports suivants échouent jusqu'au
  redémarrage de l'appli). Il faut englober `Start()` dans un try/catch qui
  réinitialise `_process`/`_stdin` en cas d'échec.
  > Corrigé : `_process` n'est assigné qu'après un `Start()` réussi ; en cas
  > d'échec le `Process` est disposé et l'exception propage sans laisser
  > `IsRunning` dans un état incohérent.

- [x] **Cache vidéo partagé ignorant la résolution → corruption mémoire GPU.**
  `src/Videotoy.Ffmpeg/VideoFrameKey.cs` (L13) définit la clé de cache comme
  `(VideoFilePath, FrameIndex)` sans la résolution cible. Le cache
  (`VideoTextureLoader.cs` L43-57) est un singleton DI partagé entre l'aperçu
  (basse résolution) et l'export (résolution potentiellement 4K). Une frame
  décodée en aperçu peut être réutilisée telle quelle lors d'un export à une
  résolution différente, et `MultiPassRenderer.RefreshDynamicAssets` (L588-620)
  calcule `rowSizeInBytes` à partir de ces dimensions erronées avant une copie
  mémoire non sécurisée (`Buffer.MemoryCopy`) vers une texture D3D11 mappée —
  risque réel de corruption/débordement mémoire, reproductible simplement en
  prévisualisant puis en exportant un shader utilisant une texture vidéo.
  > Corrigé : `VideoFrameKey` inclut désormais `TargetWidth`/`TargetHeight` ;
  > deux résolutions différentes du même fichier/frame n'entrent plus en
  > collision dans le cache (tests de régression ajoutés).

## Élevé

- [x] **Erreur "disque plein" reclassée à tort comme transitoire.**
  `src/Videotoy.Ffmpeg/TransientFfmpegErrorClassifier.cs` (L13-20) traite toute
  `IOException` comme transitoire, alors que le commentaire de la classe indique
  explicitement qu'un disque plein ne doit jamais être retenté. Un disque plein
  remonte comme `IOException` (pipe brisé) depuis `FfmpegService.WriteFrameAsync`
  avant que `FfmpegStderrDiagnosis` puisse le classifier correctement, ce qui fait
  relancer inutilement tout le pipeline de rendu (`VideoExportPipeline.RunAsync`,
  jusqu'à `MaxTransientRetries` fois) pour un export voué à l'échec.
  > Corrigé : `WriteFrameAsync`/`FinishAsync` traduisent désormais une rupture
  > de pipe causée par la fin du process en `FfmpegEncodingException`
  > diagnostiquée (stderr réellement analysé) plutôt qu'une `IOException`
  > brute ; le classifieur ne traite plus jamais une `IOException` comme
  > transitoire (tests de régression ajoutés).

- [x] **Vérification d'intégrité FFmpeg (SHA-256) faite une seule fois, TOCTOU.**
  `src/Videotoy.Ffmpeg/FfmpegIntegrityVerifier.cs` (L14-40) n'est appelée qu'au
  démarrage (`App.xaml.cs` L42-58). Aucun des nombreux appels ultérieurs à
  `Process.Start()` (export, décodage vidéo, sonde matérielle) ne revérifie le
  binaire. Un remplacement du fichier après le démarrage (mise à jour ratée,
  logiciel malveillant, antivirus) passerait inaperçu.
  > Corrigé : nouvelle méthode `EnsureStillValid()` appelée avant chaque
  > lancement de `ffmpeg.exe` (`FfmpegService`, `VideoFrameDecoder`,
  > `VideoProber`, `HardwareEncoderProbe`) ; ne recalcule le SHA-256 complet
  > que si la date de modification ou la taille du fichier a changé depuis la
  > dernière vérification, pour rester bon marché sur un export long.

- [ ] **Flags CLI de `tint.exe` non vérifiés (TODO explicite de l'auteur).**
  `src/Videotoy.Transpiler/WgslTranspilerProcess.cs` (L47-55) contient un TODO
  indiquant que les flags (`--format hlsl -o ...`) n'ont jamais été vérifiés
  contre le binaire réel qui sera distribué. Risque d'échec silencieux ou de
  comportement différent selon la version de `tint.exe` embarquée — à tester
  contre le binaire réellement livré dans `tools/tint/`.
  > Non vérifiable dans cet environnement (pas de binaire `tint.exe`, pas de
  > Windows/SDK disponibles pour l'exécuter). L'invocation actuelle
  > correspond à la CLI documentée du projet Dawn/Tint et n'a donc pas été
  > changée ; commentaire mis à jour pour expliquer où regarder en premier si
  > le chargement WGSL échoue systématiquement une fois le binaire en place.
  > **Reste à faire manuellement** : lancer `tint.exe --help` avec le binaire
  > réel et confirmer les flags avant la première release avec support WGSL.

- [x] **`iMouse`, `iDate` et `iChannelResolution` toujours à zéro.**
  `src/Videotoy.Rendering/D3D11ShaderRenderer.cs` (L122-138) et
  `MultiPassRenderer.cs` (L631-647, code dupliqué) fixent ces uniformes
  Shadertoy standards à `Vector4.Zero`. Tout shader importé qui utilise
  l'interaction souris, la date, ou la résolution par canal (usages très
  courants sur Shadertoy) rendra un résultat visuellement incorrect, sans
  aucun diagnostic pour l'utilisateur.
  > `iChannelResolution0-3` corrigé : calculé pour de vrai par passe à partir
  > des textures effectivement liées (buffer d'une autre passe ou asset
  > image/vidéo/spectre audio). `iMouse`/`iDate` restent **volontairement**
  > à zéro : ce sont les deux seuls uniformes Shadertoy intrinsèquement non
  > déterministes (horloge murale, interaction temps réel), et ce projet
  > garantit explicitement un pipeline de rendu déterministe — les rendre
  > réels romprait cette garantie (deux exports du même shader à des
  > instants différents produiraient des vidéos différentes). Voir le
  > commentaire ajouté dans `MultiPassRenderer.UpdateUniforms`.
  > Le renderer mono-passe dupliqué (`D3D11ShaderRenderer`/`NullShaderRenderer`/
  > `IShaderRenderer`), confirmé mort (item Faible ci-dessous), a été
  > supprimé plutôt que corrigé en double.

- [x] **Boucle d'export de la file de rendu exécutée sur le thread UI.**
  `src/Videotoy.App/ViewModels/MainWindowViewModel.cs` (`StartRenderQueueAsync`,
  ~L1577-1593) appelle `_renderQueueProcessor.StartAsync` sans `Task.Run`, et
  aucun `ConfigureAwait(false)` n'est utilisé dans toute la chaîne d'appels
  (`RenderQueueProcessor.cs` L245-310, `VideoExportPipeline.cs` L138-140). Le
  rendu D3D11 et la lecture des pixels (`Map` bloquant) s'exécutent donc entre
  les `await` sur le thread UI, provoquant des blocages/saccades pendant toute
  la durée d'un export par lot, surtout en haute résolution.
  > Corrigé à la racine : `FrameSequenceRenderer.RenderSequence` est devenu un
  > `IAsyncEnumerable` où chaque rendu de frame passe par `Task.Run`,
  > garantissant qu'il ne s'exécute jamais en ligne sur l'appelant — y
  > compris quand un `await` voisin se termine de façon synchrone et ne cède
  > donc pas la main de lui-même. Les trois pipelines d'export (vidéo, image
  > animée, séquence d'images) mis à jour en conséquence.

- [x] **Aucun gestionnaire global d'exceptions non gérées.** Recherche sur tout
  le dépôt : ni `DispatcherUnhandledException`, ni
  `AppDomain.CurrentDomain.UnhandledException`, ni
  `TaskScheduler.UnobservedTaskException` (`src/Videotoy.App`). Combiné aux
  `async void` non protégés (voir plus bas), toute exception échappée plante
  l'application entière sans message ni log de crash.
  > Corrigé : les trois gestionnaires ajoutés dans `App()`, avec message
  > utilisateur clair et trace (`System.Diagnostics.Trace`) au lieu d'un
  > plantage silencieux.

- [x] **Aucun projet de tests dans la solution.** `Videotoy.sln` ne référence
  que les 6 projets applicatifs, aucun projet de test. Zéro couverture
  automatisée pour la logique F# (`ShadertoyJsonParser`, `ShaderValidator`,
  `PassGraph`, `LoopCalculator`), la construction des arguments FFmpeg
  (`FfmpegService.BuildArguments`), `FfmpegStderrParser` /
  `TransientFfmpegErrorClassifier` (dont le bug 1.2 aurait été détecté par un
  simple test unitaire), ou les pipelines de rendu/export. Point structurant à
  traiter en priorité pour fiabiliser le reste.
  > Nouveau projet `tests/Videotoy.Tests` (xUnit) ajouté à `Videotoy.sln` et
  > au workflow CI (`dotnet test`). Couvre pour l'instant les deux bugs
  > corrigés dans cette passe (classification des erreurs FFmpeg transitoires,
  > égalité de `VideoFrameKey` par résolution) et la robustesse du parseur
  > JSON Shadertoy face à des champs mal typés. Reste à étoffer
  > (`ShaderValidator`, `PassGraph`, `LoopCalculator`, `BuildArguments`...) —
  > non exécutable/vérifiable dans cet environnement (pas de SDK .NET/Windows
  > disponible ici), à valider avec `dotnet test` sur une machine de dev.

## Moyen

- [x] **`ShadertoyJsonParser` plante sur JSON malformé mais valide.**
  `src/Videotoy.Core/ShadertoyJsonParser.fs` : `tryGetStringValue` (L24-27) et
  `parseChannel` (L60-63) appellent `GetString()`/`GetInt32()` sans vérifier le
  type du `JsonElement`, ce qui lève `InvalidOperationException` non interceptée
  (seul `JsonException` est capturé, L144-146). Un fichier `.shadertoy`/`.json`
  partagé/téléchargé avec un champ mal typé plante le flux "Ouvrir un shader" au
  lieu d'afficher un message d'erreur clair.
  > Corrigé : les deux accesseurs vérifient désormais `ValueKind`/utilisent
  > `TryGetInt32` au lieu de lever ; un filet de sécurité `InvalidOperationException`
  > a aussi été ajouté au `try/with` racine. Tests de régression ajoutés.

- [x] **Traversée de répertoire (path traversal) dans la résolution des assets
  de shader.** `src/Videotoy.Media/ShaderFileService.cs` (`ResolveAssetPath`,
  L246-249) ne normalise ni ne contraint le chemin résolu au répertoire de
  base : un `iChannel` avec `"src": "../../../../Windows/win.ini"` ou un chemin
  absolu est accepté sans validation, permettant de charger un fichier arbitraire
  du disque comme texture/audio/vidéo (mêmes points d'entrée dans `LoadTexture`,
  `LoadAudio`, `LoadVideo`).
  > Corrigé : `TryResolveAssetPath` normalise et vérifie désormais que le
  > chemin résolu reste sous le répertoire du fichier shader chargé ; sinon
  > l'asset est refusé avec un message explicite dans le panneau "Shader
  > Issues" plutôt que chargé silencieusement.

- [x] **Aucune gestion de la perte de périphérique GPU (DXGI device
  removed/TDR).** Aucune référence à `DeviceRemoved`, `GetDeviceRemovedReason`
  ou `DXGI_ERROR_DEVICE_REMOVED` dans `src/Videotoy.Rendering`. Un crash pilote,
  un timeout TDR, ou un changement de GPU pendant un export long produira une
  exception bas niveau opaque plutôt qu'un message clair "GPU perdu, veuillez
  réessayer".
  > Corrigé pragmatiquement : `OffscreenRenderContext.ReadPixelsRgba` et
  > `MultiPassRenderer.RenderFrame` traduisent désormais toute
  > `SharpGen.Runtime.SharpGenException` (l'exception standard pour un HRESULT
  > D3D11/DXGI en échec) en `GpuDeviceLostException`, avec un message
  > actionnable. N'identifie pas précisément "device removed" via l'API bas
  > niveau `GetDeviceRemovedReason` (API Vortice exacte non vérifiable hors
  > Windows/sans le SDK dans cet environnement), mais couvre le même
  > symptôme utilisateur (pilote qui plante, TDR, GPU changé).

- [x] **`App.OnStartup` ne protège que la vérification FFmpeg.**
  `src/Videotoy.App/App.xaml.cs` (L31-70) : après le try/catch dédié à
  l'intégrité FFmpeg, `Services.GetRequiredService<Views.MainWindow>()`
  construit tout le graphe DI (dont la création du device D3D11) sans aucune
  protection. Un échec ici (pas de GPU/driver utilisable, DLL manquante) plante
  l'appli avec la boîte de dialogue générique Windows au lieu d'un message
  convivial comme pour le cas FFmpeg.
  > Corrigé : résolution de `MainWindow` enveloppée dans un try/catch dédié
  > avec message utilisateur clair et arrêt propre (`Shutdown(1)`).

- [x] **Nom de fichier de sortie non protégé contre la confusion avec un flag
  CLI.** `src/Videotoy.Ffmpeg/FfmpegService.cs` (L506, L228, L545, L555, L572) :
  le chemin de sortie est passé en dernier argument positionnel sans séparateur
  `--` ni validation. Un nom de fichier commençant par `-` serait interprété par
  FFmpeg comme une option.
  > Corrigé : tous les chemins de sortie (vidéo, séquence d'images, palette/
  > sortie GIF, WebP) passent par `SanitizeOutputPath` (`Path.GetFullPath`),
  > qui garantit un chemin absolu — jamais susceptible de commencer par `-`.

## Faible

- [x] **Échec de sonde matérielle avalé silencieusement.**
  `src/Videotoy.Ffmpeg/HardwareEncoderProbe.cs` (L180-185) : l'exception est
  entièrement ignorée (juste un commentaire), rendant les problèmes de détection
  d'encodage matériel impossibles à diagnostiquer depuis les rapports de bug
  utilisateurs. Ajouter au moins un log.
  > Corrigé : trace `Trace.TraceWarning` ajoutée (pas de dépendance de
  > logging dans ce projet, `System.Diagnostics.Trace` réutilise l'existant).

- [x] **Fuite transitoire de shaders pixel COM en cas d'échec de compilation
  partiel.** `src/Videotoy.Rendering/MultiPassRenderer.cs` (`BuildPassGraph`,
  L288-340) : si la compilation d'une passe échoue après que des passes
  précédentes ont déjà créé leurs `ID3D11PixelShader`, ceux-ci ne sont libérés
  qu'au prochain chargement réussi ou à la fermeture de l'appli, pas
  immédiatement.
  > Corrigé : `BuildPassGraph` appelle `DisposeSlots()` immédiatement dans un
  > `catch` si une passe échoue à compiler, avant de relancer l'exception.

- [x] **Gestionnaire `async void` non protégé pour le glisser-déposer vidéo.**
  `src/Videotoy.App/Views/MainWindow.xaml.cs` (`OnVideoChannelDrop`, L217-237) :
  toute exception dans `HandleFileDroppedAsync` est inobservable et, faute de
  gestionnaire global (voir plus haut), plante l'application.
  > Corrigé : try/catch local avec message utilisateur (fichier vidéo
  > invalide n'importe plus toute l'application) ; au passage, retrait du
  > `ConfigureAwait(false)` dans `VideoChannelViewModel.HandleFileDroppedAsync`
  > qui faisait s'exécuter des mises à jour liées à l'UI hors du thread UI.

- [x] **Chaîne de localisation orpheline.**
  `src/Videotoy.App/Resources/Localization/en.json` et `fr.json` (L28) :
  `viewport.shaderLoaded.previewPending` n'est référencée nulle part dans le
  code alors que l'aperçu en direct est bien implémenté — probablement un
  reliquat d'un état "à venir" jamais nettoyé. À vérifier/supprimer, et auditer
  d'autres restes similaires de cette transition.
  > Supprimée des deux fichiers.

- [x] **Duplication possible de logique de rendu mono-passe inutilisée.**
  `src/Videotoy.Rendering/D3D11ShaderRenderer.cs` et `NullShaderRenderer.cs` :
  aucune référence à `IShaderRenderer`/`D3D11ShaderRenderer`/`NullShaderRenderer`
  trouvée dans `src/Videotoy.App` (seul `MultiPassRenderer` semble câblé). Si
  confirmé mort, ce code duplique aussi le bug des uniformes figés à zéro
  (voir ci-dessus) et devrait être supprimé ou fusionné.
  > Confirmé mort (aucune référence dans tout le dépôt) et supprimé
  > (`D3D11ShaderRenderer.cs`, `IShaderRenderer.cs`, `NullShaderRenderer.cs`).

- [x] **`MainWindowViewModel` ne se désabonne jamais de ses événements.**
  `src/Videotoy.App/ViewModels/MainWindowViewModel.cs` (L1002-1008) : quatre
  abonnements (`_previewClock.TimeChanged`, `_renderQueueProcessor.*`,
  `_historyStack.StateChanged`) sans `IDisposable` correspondant. Sans impact
  aujourd'hui (singleton vivant toute la durée de l'appli), mais deviendrait une
  fuite mémoire classique WPF si une seconde fenêtre/document était introduite.
  > Corrigé : `MainWindowViewModel` implémente désormais `IDisposable` et se
  > désabonne de `_renderQueueProcessor` (le seul service partagé/injecté
  > parmi les quatre — `_previewClock`/`_historyStack` sont possédés en
  > exclusivité par le ViewModel, jamais un vrai risque de fuite).
  > `App.OnExit` dispose le conteneur DI, ce qui déclenche ce `Dispose()`.

- [x] **Quelques chaînes françaises non traduites.**
  `src/Videotoy.App/Resources/Localization/fr.json` : certaines chaînes restent
  identiques à l'anglais alors qu'une traduction serait attendue, par ex.
  `statusBar.currentFrame` (`"Frame {0}"` → `"Image {0}"`) et
  `statusBar.frameCount.label` (`"Frame "`).
  > Corrigé (+ deux occurrences supplémentaires du même mot repérées lors du
  > passage : `panel.durationMode.unit.frames`, `export.progress.frameCount`).

---

# Roadmap — Nouvelles phases : compatibilité Shadertoy 100% + éditeur de code intégré

Les phases ci-dessous s'ajoutent à la liste de correctifs précédente. Elles ne
sont pas classées par sévérité de bug mais par dépendances logiques entre
elles (une phase peut s'appuyer sur la précédente). Chaque item liste les
projets/fichiers concernés à titre indicatif, à ajuster à l'implémentation
réelle.

## Phase 1 — Éditeur de code intégré

Aujourd'hui Videotoy n'ouvre que des fichiers depuis le disque
(**File → Open Shader...** / drag & drop) ; il n'existe aucun moyen de lire
le code importé ni d'en écrire/coller directement dans l'application.

- [x] **Panneau éditeur avec coloration syntaxique GLSL/HLSL/WGSL.** Nouveau
  contrôle dans `Videotoy.App/Views/` (ex. `ShaderEditorView.xaml`), avec
  coloration syntaxique (mots-clés GLSL/HLSL/WGSL, types vectoriels,
  qualificatifs `uniform`/`in`/`out`, commentaires, littéraux), numérotation
  de ligne, et repli de code par passe (`Image`/`Buffer A-D`/`Common`) pour
  les projets multi-passes. Évaluer `AvalonEdit` (déjà répandu en WPF, léger,
  pas de dépendance native) plutôt qu'un contrôle maison.
  > Implémenté avec `AvalonEdit` (nouvelle dépendance `Videotoy.App.csproj`) :
  > `Views/ShaderEditorView.xaml(.cs)` encapsule un `TextEditor` avec
  > coloration syntaxique dédiée par langage (`Resources/Editor/Glsl.xshd`,
  > `Hlsl.xshd`, `Wgsl.xshd`) et numérotation de ligne native AvalonEdit.
  > **Repli de code par passe non fait** : remplacé par des onglets par passe
  > (point "Édition par passe" ci-dessous) plutôt qu'un unique document replié
  > — couvre le même besoin (naviguer/isoler chaque passe) sans complexité de
  > repli de code superposée aux onglets. Boutons du panneau (Nouveau/
  > Enregistrer/Enregistrer sous/Compiler) en icônes SVG (`IconNewFile`/
  > `IconSave`/`IconSaveAs`/`IconPlay`, `Resources/Icons.xaml`) plutôt qu'en
  > texte, cohérent avec le reste de la toolbar de l'appli.
- [x] **Ouverture d'un shader importé directement dans l'éditeur.** Le
  fichier ouvert via **File → Open Shader...** ou le drag & drop remplit
  l'éditeur avec son contenu (au lieu de, ou en plus de, compiler
  directement) : l'utilisateur voit et peut modifier le code source avant
  compilation/aperçu.
  > `MainWindowViewModel.LoadShaderFile` appelle désormais
  > `PopulateEditorFromProject` après chaque chargement réussi, qui
  > reconstruit les onglets de l'éditeur (un par passe existante) à partir du
  > projet chargé.
- [x] **Saisie/collage de code directement dans l'appli, sans fichier.**
  Nouvelle entrée **File → New Shader...** (ou raccourci) qui ouvre un
  éditeur vide (ou un template Shadertoy minimal `mainImage`) sans passer
  par un fichier sur disque ; possibilité de coller du code copié depuis le
  site Shadertoy et de lancer l'aperçu sans jamais sauvegarder.
  > `NewShaderCommand` (menu **File → New Shader**, `MainWindowViewModel.Editor.cs`)
  > crée un projet en mémoire avec un template `mainImage` minimal, sans
  > toucher au disque, ouvre directement le panneau éditeur, et permet de
  > coller/compiler sans jamais sauvegarder.
- [x] **Compilation/aperçu à la volée depuis l'éditeur.** Bouton ou
  raccourci (`Ctrl+Entrée` par ex.) qui relance
  `MultiPassRenderer`/`ShaderValidator` sur le contenu actuel de l'éditeur
  (pas seulement sur un fichier rechargé depuis le disque), avec les erreurs
  de compilation remontées dans le panneau **Shader Issues** existant,
  ancrées sur les numéros de ligne de l'éditeur.
  > `CompileFromEditorCommand` (bouton "Compile" + `Ctrl+Entrée`) reconstruit
  > le `ShaderProject` avec le contenu actuel de chaque onglet
  > (`ShaderModel.withPassSourceCode`, nouvelle fonction pure côté `Core`) et
  > relance validation → transpilation → aperçu via la nouvelle méthode
  > `ShaderFileService.LoadFromProject`. Les erreurs/avertissements de
  > `ShaderIssues` filtrés sur la passe active sont surlignés directement
  > dans la marge de l'éditeur (`ShaderEditorView.ErrorLines`/`WarningLines`).
- [x] **Sauvegarde depuis l'éditeur.** **File → Save** / **Save As...** pour
  écrire le contenu actuel de l'éditeur vers un fichier (nouveau ou
  existant), y compris quand le shader a été créé directement dans l'appli
  (Phase 1, point précédent) sans fichier d'origine.
  > `SaveShaderCommand`/`SaveShaderAsCommand` ajoutés (menu **File** +
  > panneau éditeur). **Portée volontairement limitée** aux projets
  > mono-fichier (`.glsl`/`.frag`/`.wgsl`/`.hlsl`/`.hlsli`, y compris un
  > nouveau shader jamais sauvegardé) : un projet JSON/Shadertoy multi-passes
  > peut être édité et compilé à la volée normalement, mais Save/Save As sont
  > désactivés pour lui faute de sérialiseur JSON Shadertoy en écriture — pas
  > dans le périmètre de cette phase, à traiter séparément si besoin.
- [x] **Indicateur de modifications non enregistrées.** Astérisque dans le
  titre de fenêtre / onglet et confirmation à la fermeture si l'éditeur
  contient des changements non sauvegardés par rapport au fichier chargé.
  > **Fait partiellement, écart assumé** : `IsEditorDirty` (comparaison au
  > dernier contenu synchronisé, pas un simple booléen "a changé une fois")
  > pilote un point visuel dans l'en-tête du panneau éditeur, pas un
  > astérisque dans le titre de la fenêtre. Aucune confirmation à la
  > fermeture de l'application n'a été ajoutée. À compléter si ce
  > comportement s'avère nécessaire en pratique.
- [x] **Édition par passe pour les projets multi-passes.** Pour un projet
  Shadertoy JSON (`ShaderModel` : Image/Buffer A-D/Common), l'éditeur doit
  permettre de basculer entre les passes (onglets ou liste déroulante) et
  d'éditer chacune indépendamment, cohérent avec le filtrage par passe déjà
  présent dans le panneau **Shader Issues**.
  > Onglets par passe (`EditorPasses`/`SelectedEditorPass`,
  > `EditorPassOptionViewModel`) — un onglet par passe existante
  > (Image/Buffer A-D/Common), édition indépendante de chacune, cohérent avec
  > `AvailablePassNames` du panneau Shader Issues.

> **Vérifié interactivement** (capture d'écran + automatisation souris/
> clavier Win32 sur une session de dev réelle, après le constat initial que
> le build seul ne suffisait pas à garantir un panneau fonctionnel) : quatre
> bugs réels ont été trouvés et corrigés à cette occasion, aucun visible à la
> seule lecture du code ou à la compilation —
> 1. **Panneau invisible/crash** : `DoubleAnimation` ne peut pas animer
>    `ColumnDefinition.Width` (type `GridLength`, pas `double`) ; le premier
>    clic sur le bouton bascule faisait planter l'appli
>    (`InvalidOperationException`, interceptée par le gestionnaire global —
>    voir item "Aucun gestionnaire global d'exceptions non gérées" plus haut
>    dans ce fichier — plutôt que de crasher silencieusement). Corrigé en
>    assignant `EditorPanelColumn.Width` directement en code-behind
>    (`MainWindow.xaml.cs`), sans `Storyboard`.
> 2. **Texte illisible** : le fond de l'éditeur (`ViewportBackgroundBrush`,
>    quasi noir) et `Foreground="{StaticResource TextPrimaryBrush}"` (gris
>    quasi noir, prévu pour un fond clair — le reste de l'appli est en thème
>    clair) donnaient un texte noir sur fond noir pour tout token non
>    explicitement coloré par un `.xshd`. Corrigé avec des couleurs
>    clair-sur-sombre dédiées (`ShaderEditorView.xaml`).
> 3. **Coloration syntaxique totalement absente** : le binding
>    `ShaderLanguage="{Binding SelectedShaderLanguage.Value}"` ne déclenchait
>    jamais son callback pour la valeur par défaut (`Glsl`), donc
>    `TextEditor.SyntaxHighlighting` restait `null` en pratique. Corrigé en
>    l'assignant explicitement dans le constructeur de `ShaderEditorView`,
>    indépendamment du binding.
> 4. **Bouton Compile inerte** : `CompileFromEditorCommand.NotifyCanExecuteChanged()`
>    n'était jamais appelé après le peuplement des onglets de l'éditeur — le
>    bouton restait visuellement normal mais WPF le gardait désactivé en
>    interne (`CanExecute` figé sur son évaluation initiale, avant tout
>    shader chargé). Corrigé en l'appelant dans `PopulateEditorFromProject`.
>
> Flux confirmé fonctionnel de bout en bout après ces corrections : ouverture
> du panneau, **New Shader** avec template rempli, coloration syntaxique
> visible et différenciée, frappe en direct avec indicateur "non enregistré"
> qui apparaît, **Compile** qui remonte une erreur de syntaxe clairement dans
> la barre de statut sans casser l'aperçu existant, puis compilation réussie
> après correction avec mise à jour de l'aperçu et disparition de
> l'indicateur "non enregistré". **Save**/**Save As** non re-testés après ces
> corrections (testés seulement structurellement avant) — à confirmer à
> l'occasion d'une prochaine session.

## Phase 2 — Support de l'extension de fichier `.txt`

- [x] **Reconnaissance de l'extension `.txt` comme code source shader.**
  Étendre la détection de langage source (README : « détection automatique
  du langage source... par extension, puis par heuristique de syntaxe pour
  les fichiers ambigus ») pour accepter `.txt` au même titre que `.glsl`,
  `.frag`, `.wgsl`, `.hlsl`, `.hlsli` : un fichier `.txt` doit passer par
  l'heuristique de détection de syntaxe (GLSL/HLSL/WGSL) plutôt que d'être
  rejeté ou traité comme non-shader. Concerne le filtre de la boîte de
  dialogue **File → Open Shader...**, la validation du drag & drop sur le
  viewport, et le service de détection de langage dans `Videotoy.Media`.
  > `Core.ShaderLanguageDetector.detect` retombait déjà sur l'heuristique de
  > contenu pour toute extension non reconnue (donc `.txt` y compris) sans
  > aucun changement — le seul blocage réel était en amont :
  > `ShaderFileService.RawExtensions` (`Videotoy.Media`) ne listait pas
  > `.txt`, donc `IsSupportedShaderFile`/`Load` le rejetaient avant même
  > d'atteindre le détecteur. `.txt` ajouté à `RawExtensions`.
- [x] **Mise à jour des filtres de fichiers dans toute l'UI.** Boîte de
  dialogue d'ouverture, zone de drop, et toute validation d'extension
  côté `Videotoy.App`/`Videotoy.Media` doivent lister `.txt` à côté des
  extensions déjà supportées (utile pour le code copié depuis Shadertoy et
  collé dans un `.txt` brut avant import).
  > `.txt` ajouté à `MainWindowViewModel.OpenFileDialogExtensions` (filtre de
  > la boîte **File → Open Shader...**) et à
  > `MainWindowViewModel.Editor.RawShaderExtensions` (pour que **Save**
  > reste actif sur un `.txt` déjà ouvert dans l'éditeur intégré — Phase 1).
  > Le drag & drop réutilise directement `ShaderFileService.IsSupportedShaderFile`,
  > donc déjà couvert par le changement ci-dessus sans modification propre.
- [x] **Documentation à jour.** Mentionner `.txt` dans `README.md` (section
  *Shader loading*) et dans le texte d'aide/onboarding, en clarifiant que
  le contenu est alors désambiguïsé par heuristique plutôt que par
  extension.
  > `README.md` mis à jour (section *Shader loading* + section *Usage*).
  > Aucune mention d'extension de fichier trouvée dans les textes
  > d'onboarding/localisation (`en.json`/`fr.json`) — rien à y changer.
  >
  > **Vérifié interactivement** : fichier `.txt` contenant un shader GLSL
  > brut ouvert via **File → Open Shader...** — chargé avec succès, langage
  > détecté "GLSL" via l'heuristique de contenu (confirmé dans la barre de
  > statut), aperçu rendu correctement.

## Phase 3 — Uniformes et variables d'entrée Shadertoy manquants

L'objectif de cette phase est la compatibilité **binaire** avec un shader
copié tel quel depuis shadertoy.com, sans aucune adaptation manuelle.

- [x] **`iChannelTime[4]`.** Absent de `UniformBuilder`/`MultiPassRenderer` ;
  nécessaire pour les shaders qui synchronisent leur logique sur la
  position de lecture de chaque canal vidéo, à calculer de façon
  déterministe à partir du mapping temporel déjà existant pour les canaux
  vidéo (README : *time-mapping (linear, looped, ou frozen)*).
  > Ajouté au cbuffer HLSL (`HlslBoilerplate.shadertoyUniformCBuffer`, un
  > `float4` par canal — l'alignement 16 octets d'un tableau HLSL l'impose
  > même si seul `.x` est réellement lu) et à `ShadertoyUniformsBuffer`
  > (128 → 192 octets). `MultiPassRenderer.ResolveChannelTimes` réutilise
  > `Core.VideoTimeMapping.resolveVideoPlaybackTimeSeconds` (déjà utilisé en
  > interne pour choisir la frame vidéo à décoder) pour exposer la même
  > valeur comme uniforme ; `BoundVideoAsset` expose désormais
  > `ResolvePlaybackTimeSeconds` séparément de `GetFramePixelsBgra` à cette
  > fin. Les canaux non-vidéo reçoivent `iTime`, comportement par défaut
  > documenté de Shadertoy.
- [x] **`iFrameRate`.** Uniforme scalaire simple (FPS cible), trivial à
  dériver de `LoopCalculator`/les réglages d'export ; actuellement absent.
  > Dérivé de `1.0 / deltaSeconds` dans `MultiPassRenderer.UpdateUniforms` :
  > `Core.LoopCalculator.buildFrameTimeline` fixe déjà `deltaSeconds` à
  > `1.0/frameRate.Value` pour chaque frame du timeline déterministe, donc
  > cette relation est exacte ici (pas une approximation) — évite de
  > propager un paramètre de FPS séparé jusqu'à `Initialize()`.
- [x] **Support `keyboard` comme type de canal `iChannel`.** Shadertoy
  permet un canal spécial "Keyboard" (texture 256×3 encodant l'état des
  touches). Hors du cadre déterministe habituel de l'export vidéo, mais
  nécessaire pour ouvrir/prévisualiser un shader qui en dépend sans erreur ;
  à défaut d'interaction réelle en aperçu, documenter clairement dans le
  panneau **Shader Issues** qu'un export utilisant ce canal produira un état
  clavier figé (vide), plutôt que d'échouer silencieusement.
  > `ShadertoyJsonParser.unsupportedByDesignInputTypeMessage` détecte
  > `"keyboard"` (et `"mic"`, item suivant) et produit un `warningIssue`
  > explicite dans le panneau Shader Issues au lieu de faire disparaître le
  > canal silencieusement (comportement précédent de `parseInputType`
  > retournant `None`). Pas de nouveau cas `ChannelInputType.Keyboard` :
  > aucune texture d'état clavier n'est chargée/rendue (canal ignoré comme
  > avant), seul le diagnostic change. Tests de régression ajoutés.
- [x] **Support `cubemap` comme type de canal `iChannel`.** Les projets
  Shadertoy utilisant une "Cube Map" comme entrée ne sont aujourd'hui pas
  couverts par le chargement de texture (`TextureLoader`, README : *Static
  image textures*). Ajouter le chargement de 6 faces et l'échantillonnage
  cube côté transpileur GLSL→HLSL.
  > Chargement : `Core.ShaderModel.cubemapFacePaths` dérive les 6 chemins de
  > face depuis le chemin de base (`"src"` de la face 0), convention
  > `xxx.png`/`xxx_1.png`/…/`xxx_5.png` ; `TextureLoader.LoadCubemap` charge
  > et valide que les 6 faces ont des dimensions identiques. Rendu :
  > `MultiPassRenderer.CreateCubemapAsset` crée un `ID3D11Texture2D`
  > `ArraySize=6`/`MiscFlags.TextureCube`, une sous-ressource par face.
  > Transpileur : `HlslBoilerplate.channelDeclarations` émet désormais
  > `TextureCube` au lieu de `Texture2D` pour un canal `Cubemap` (le site
  > d'appel `texture(iChannelN, dir)` → `iChannelN.Sample(...)` n'a pas
  > besoin de changer, `.Sample(sampler, coord)` étant syntaxiquement
  > identique pour `Texture2D`/`TextureCube`/`Texture3D`).
  > **Convention de nommage des 6 faces non vérifiée contre un export
  > shadertoy.com réel** (aucun disponible au moment de l'implémentation) —
  > documentée comme telle dans `cubemapFacePaths`, à confirmer/ajuster dès
  > qu'un tel export est disponible pour test.
- [x] **Support `volume`/texture 3D comme type de canal `iChannel`.** De
  façon similaire, certains shaders Shadertoy utilisent une texture 3D
  ("Volume") en entrée ; actuellement non supporté.
  > Nouveau cas `ChannelInputType.Volume` (`"volume"` dans le JSON). Chargement :
  > `TextureLoader.LoadVolume` décode l'image atlas (supposée être une bande
  > horizontale de tranches carrées,
  > `Core.ShaderModel.volumeSliceCountFromAtlasDimensions` déduit le nombre
  > de tranches de `largeur/hauteur`) et ré-extrait chaque tranche en un
  > bloc contigu. Rendu : `MultiPassRenderer.CreateVolumeAsset` crée un
  > `ID3D11Texture3D`. Transpileur : `channelDeclarations` émet `Texture3D`
  > pour un canal `Volume`. **Convention de décodage de l'atlas non vérifiée
  > contre un export shadertoy.com réel** (aucun disponible), documentée
  > comme telle et à confirmer/ajuster dès qu'un tel export est disponible.
- [x] **`iChannel` de type "Music" vs "Mic"/entrée micro en direct.**
  Shadertoy distingue plusieurs sources audio (fichier musical, micro,
  aucune) ; seul le fichier audio (WAV/MP3/OGG) est couvert aujourd'hui.
  Une entrée micro n'a pas de sens dans un pipeline déterministe : à
  documenter explicitement comme non supporté par design (plutôt que comme
  un oubli) dans le panneau **Shader Issues**, avec message clair au lieu
  d'un échec de chargement générique.
  > Même mécanisme que "Keyboard" ci-dessus (`unsupportedByDesignInputTypeMessage`) :
  > `"mic"` produit désormais un `warningIssue` explicite ("cannot be used
  > in a deterministic export pipeline; use a music file input instead")
  > plutôt qu'une disparition silencieuse du canal. Tests de régression
  > ajoutés.
- [x] **Sortie multi-buffers `gl_FragColor` vs `out vec4 fragColor`
  (variantes de syntaxe d'entrée).** Vérifier que le transpileur
  (`GlslToHlslTranspiler`) accepte aussi bien la forme historique Shadertoy
  (`mainImage(out vec4 fragColor, in vec2 fragCoord)`) que des variantes
  ponctuellement rencontrées dans des shaders copiés/collés (espacement,
  ordre des qualificatifs) sans erreur de parsing superficielle.
  > Déjà correct avant cette phase : la regex de
  > `HlslBoilerplate.renameMainImage` tolère déjà l'absence du qualificatif
  > `in`, les espaces/retours à la ligne superflus, et des noms de
  > paramètres arbitraires. Un shader historique n'utilisant que
  > `gl_FragColor` sans jamais déclarer `mainImage` produit déjà un message
  > clair ("Missing 'mainImage' entry point"). Aucun changement de code
  > nécessaire — seuls des tests de régression ont été ajoutés
  > (`GlslToHlslTranspilerTests`) pour figer ce comportement et éviter une
  > régression silencieuse future.
- [x] **Résolution des liaisons inter-passes par `id` opaque plutôt que par
  nom de buffer.** Un export JSON réel obtenu depuis l'export officiel de
  shadertoy.com (voir `shader_s3tGzN.json` fourni comme cas de test,
  passe "Image" ↔ "Buffer A") relie ses passes ainsi : chaque
  `renderpass[].outputs[]` porte un `id` alphanumérique arbitraire (ex.
  `"4dXGR8"`), et le `renderpass[].inputs[]` d'une autre passe qui lit ce
  buffer référence ce même `id` — le nom lisible (`"Buffer A"`) n'apparaît
  que dans `renderpass[].name`, jamais dans le lien `inputs`/`outputs`
  lui-même. Le `filepath` associé à un input de type `"buffer"` (ex.
  `/media/previz/buffer00.png`) n'est qu'un aperçu miniature côté
  shadertoy.com, pas la source réelle à charger. `ShadertoyJsonParser`
  doit donc résoudre le graphe de dépendances par correspondance
  `outputs[].id` ↔ `inputs[].id` (en construisant une table `id → nom de
  passe` à partir de tous les `renderpass[].outputs[].id` avant de
  résoudre les `inputs`), et seulement retomber sur une correspondance par
  nom de buffer (`"Buffer A"`/`"BufferA"`, déjà gérée par `PassGraph`
  selon le CHANGELOG) pour les exports plus anciens ou construits/édités
  à la main qui n'auraient pas cet `id`. À défaut, un shader utilisant ce
  format d'export réel échoue silencieusement à retrouver son buffer
  source, ou pire, tente de charger `buffer00.png` comme une texture
  statique. Ajouter `shader_s3tGzN.json` (une fois les données
  personnelles/URL de l'auteur éventuellement neutralisées) comme fixture
  de test de non-régression dans `tests/Videotoy.Tests`.
  > Confirmé et corrigé : `parseChannelInput` résolvait un `Buffer` via
  > `"src"` uniquement (le chemin miniature, jamais le vrai nom de buffer,
  > pour un export réel). `ShadertoyJsonParser.buildOutputIdToPassNameMap`
  > construit désormais la table `id → nom de passe` depuis tous les
  > `renderpass[].outputs[].id` avant de résoudre les `inputs[]` ; un input
  > `Buffer` résout d'abord par `id` (`resolveBufferReference`), et ne
  > retombe sur `"src"` que si aucun `id` n'était présent (exports anciens/
  > édités à la main). `shader_s3tGzN.json` non obtenu (pas d'accès à un
  > export shadertoy.com réel dans cet environnement) — remplacé par deux
  > tests de régression synthétiques mais fidèles au format réel décrit
  > ci-dessus (liaison par `id`, et repli par `src` sans `id`).
- [x] **Attributs `sampler` par input (`filter`, `wrap`, `vflip`, `srgb`,
  `internal`).** Chaque entrée `inputs[]` d'un export Shadertoy porte ses
  propres réglages d'échantillonnage (ex. `"filter": "linear"`, `"wrap":
  "clamp"`, `"vflip": "true"`, `"srgb": "false"`, `"internal": "byte"`).
  Vérifier que `ShadertoyJsonParser`/`TextureLoader` appliquent
  effectivement ces réglages par canal (filtrage bilinéaire vs point,
  mode d'adressage clamp/repeat, inversion verticale, espace colorimétrique
  sRGB vs linéaire) plutôt que d'utiliser un réglage global fixe pour
  tous les `iChannel`, sous peine de rendu visuellement différent de
  l'original (texture inversée, bandes de répétition inattendues, etc.).
  > Confirmé : aucun de ces attributs n'était lu (seuls `type`/`src`
  > l'étaient) et un unique `SamplerState` partagé (filtrage bilinéaire,
  > adressage `Wrap`) était utilisé pour tous les canaux. `filter`/`wrap`/
  > `vflip` implémentés : `ChannelSamplerSettings` (nouveau, `ShaderModel.fs`)
  > porté par `ChannelSource.Sampler`, parsé depuis `inputs[].sampler.*`
  > (`ShadertoyJsonParser.parseSamplerSettings`) ; `MultiPassRenderer`
  > crée/met en cache un `ID3D11SamplerState` par combinaison filtre/mode
  > d'adressage réellement utilisée (`GetOrCreateSamplerState`) au lieu d'un
  > état partagé unique ; `vflip` retourne les lignes de l'image au
  > chargement (`TextureLoader.Load`, nouveau paramètre `verticalFlip`,
  > `false` par défaut pour préserver le comportement historique).
  > **`srgb` et `internal` non implémentés** : `srgb` nécessiterait que le
  > format de la texture D3D11 varie par canal alors qu'une texture est
  > aujourd'hui partagée (un seul upload) entre tous les canaux qui la
  > référencent — changement de format par vue nécessiterait soit dupliquer
  > la texture par combinaison (chemin, srgb), soit une texture typeless
  > avec vues multiples ; jugé trop risqué pour cette passe sans schéma de
  > test réel pour valider visuellement le résultat. `internal` (profondeur
  > "byte" vs "float") nécessiterait un chemin de décodage HDR entièrement
  > nouveau dans `TextureLoader` (aujourd'hui BGRA32 8-bit uniquement).
  > Les deux restent des gaps documentés, à traiter dans une passe séparée.

> **Vérification** : `dotnet build`/`dotnet test` propres (22/22 tests,
> 8 nouveaux ajoutés pour cette phase) après chaque item. Vérifié
> interactivement (capture d'écran + automatisation souris/clavier) que le
> pipeline de rendu standard (texture 2D, pas de régression sur le chemin
> `AssetKind.Image`/`BoundAsset` largement retouché par le passage à un type
> `Resource` commun 2D/3D/cube) fonctionne toujours normalement après
> l'ensemble des changements de cette phase — un nouveau shader se compile et
> s'affiche sans erreur. **Non vérifié visuellement** : cubemap et texture
> volume elles-mêmes (aucun export shadertoy.com réel utilisant l'un ou
> l'autre disponible dans cet environnement pour un test de bout en bout) ;
> les conventions de format de fichier (`cubemapFacePaths`,
> `volumeSliceCountFromAtlasDimensions`) sont documentées comme hypothèses à
> confirmer dès qu'un tel export sera disponible.

## Phase 4 — Fonctions et constructions GLSL manquantes ou partiellement transpilées

- [x] **Audit systématique de `GlslToHlslTranspiler` contre le sous-ensemble
  GLSL ES 3.0 utilisé par Shadertoy.** Établir une liste de référence
  (fonctions built-in : `mod`, `fract`, `mix`, `smoothstep`, `clamp`,
  matrices `mat2/mat3/mat4`, `texture`/`textureLod`/`texelFetch`,
  qualificatifs `const`, tableaux de taille fixe, structures, boucles
  `for` à borne non constante) et couvrir les manques un par un avec tests
  de non-régression dans `tests/Videotoy.Tests`.
  > Audit fait par compilation FXC réelle d'un shader synthétique exerçant
  > systématiquement chaque construction listée (structures, tableaux de
  > taille fixe, boucle `for` à borne non constante calculée depuis `iTime`,
  > `clamp`/`smoothstep`, `mat2`, fonction utilisateur, `break`). Trois bugs
  > réels trouvés et corrigés dans `GlslToHlslTranspiler.fs`, tous absents de
  > toute couverture de test avant cette phase :
  > 1. **`texelFetch`/`textureLod` non définis** : renommés en
  >    `__texelFetch`/`__textureLod` mais jamais déclarés nulle part dans le
  >    HLSL généré — tout shader les utilisant échouait à la compilation
  >    (identifiant non déclaré). Corrigé par des fonctions wrapper générées
  >    par canal (`HlslBoilerplate.channelHelperFunctionDeclarations`,
  >    `__texelFetchN`/`__textureLodN`) utilisant `.Load`/`.SampleLevel`.
  > 2. **`mat2 * vec2` (multiplication matricielle) traduit en `*` HLSL**,
  >    qui n'effectue qu'une multiplication composante-par-composante en
  >    HLSL (`error X3020: type mismatch` constaté). Corrigé par
  >    `rewriteMatrixVectorMultiplication` : réécrit `<matrice> * <expr>`/
  >    `<expr> * <matrice>` en `mul(...)` pour toute variable détectée comme
  >    matricielle (`float2x2`/`float3x3`/`float4x4`) par déclaration locale
  >    — heuristique par nom de variable, ne couvre pas une matrice retournée
  >    inline par une fonction et multipliée sans être stockée dans une
  >    variable nommée.
  > 3. **Diffusion scalaire (`vec3(0.0)` → `float3(0.0, 0.0, 0.0)`) jamais
  >    appliquée à un appel imbriqué dans un constructeur à plusieurs
  >    arguments** (`float4(float3(glow) + x, 1.0)`) : le scan à parenthèses
  >    équilibrées recopiait tout le contenu de l'appel englobant tel quel
  >    dès qu'il avait plusieurs arguments top-level, sans jamais ré-examiner
  >    séparément l'appel imbriqué (`error X3014: incorrect number of
  >    arguments` constaté). Corrigé en rendant
  >    `expandScalarVectorConstructors` récursif sur le contenu de tout appel
  >    qui n'est lui-même pas développé. A aussi nécessité de distinguer un
  >    identifiant nu scalaire (`vec3(monFloat)`, à diffuser) d'un identifiant
  >    nu déjà vectoriel (`ivec2(fragCoord)`, à convertir sans dupliquer,
  >    régression potentielle introduite puis corrigée dans la même passe) —
  >    voir `isBareIdentifierAlreadyVectorSized`, une heuristique par
  >    déclaration locale scalaire connue plutôt qu'un vrai système de types.
  > `mod`/`fract`/`mix`/`clamp`/`smoothstep`/structures/tableaux de taille
  > fixe/boucles à borne non constante étaient déjà corrects (aucun code
  > dédié nécessaire, syntaxe quasi identique GLSL/HLSL ou déjà remappée) —
  > confirmé par le même test de compilation réelle plutôt que supposé.
  > 8 nouveaux tests de régression ajoutés dans `GlslToHlslTranspilerTests`.
- [x] **Fonctions de bruit/hash courantes non natives à GLSL mais quasi
  systématiques dans les shaders Shadertoy réels.** Ne pas les
  réimplémenter en dur (ce sont des choix d'auteur), mais s'assurer que
  rien dans le transpileur/la compilation HLSL n'empêche leur définition
  arbitraire par l'utilisateur (fonctions récursives limitées, tableaux de
  constantes de grande taille, etc.).
  > Vérifié : aucun traitement spécifique n'existe pour les définitions de
  > fonction utilisateur, `const`, ou la récursion dans le transpileur — ces
  > constructions passent telles quelles, syntaxe quasi identique entre GLSL
  > et HLSL. Le shader d'audit ci-dessus définit et appelle une fonction
  > utilisateur (`sdSphere`) avec succès, confirmant qu'aucun obstacle
  > n'existe pour ce type de code défini par l'auteur.
- [x] **`#define` et macros de préprocesseur.** Vérifier la couverture des
  macros avec paramètres, macros multi-lignes (`\` de continuation), et
  `#if`/`#ifdef` conditionnels parfois utilisés pour des variantes de
  shader (qualité basse/haute) — fréquents sur Shadertoy.
  > Vérifié par compilation FXC réelle d'un shader utilisant `#define` simple
  > et paramétré (`#define TINT(c) (c * BRIGHTNESS)`) et un bloc `#ifdef`/
  > `#endif` : compile et rend correctement sans aucun changement de code.
  > FXC (invoqué en aval par `MultiPassRenderer.Compiler.Compile`) a son
  > propre préprocesseur C natif qui gère ces directives lui-même — le
  > transpileur ne les touche jamais et n'a pas besoin de le faire.
- [x] **`#include` inter-passes (Common).** Confirmer que le contenu de la
  passe `Common` (déjà modélisée dans `ShaderModel`) est effectivement
  préfixé/injecté dans chaque autre passe avant transpilation, comme le
  fait Shadertoy nativement, y compris pour les fonctions et structures
  partagées.
  > Déjà correct avant cette phase : `transpilePass` préfixe systématiquement
  > `commonCode + "\n" + pass.SourceCode` avant toute transformation.
  > Confirmé par un nouveau test (`Transpile_CommonCode_IsPrefixedBeforePassSource`)
  > qui vérifie qu'une fonction définie dans Common apparaît bien avant son
  > site d'appel dans le HLSL généré.

> **Vérification** : `dotnet build`/`dotnet test` propres (31/31 tests, 8
> nouveaux pour cette phase). Les trois bugs réels (texelFetch/textureLod non
> déclarés, mat*vec composante-par-composante, diffusion scalaire imbriquée
> non développée) ont chacun été confirmés cassés puis corrigés par
> compilation FXC réelle via l'application (fichiers `.txt` de test chargés
> par **File → Open Shader...**, pas seulement par les tests unitaires qui ne
> compilent jamais le HLSL généré) — un shader combinant struct, tableau de
> taille fixe, boucle à borne non constante, `clamp`/`smoothstep`, rotation
> matricielle et fonction utilisateur compile et s'affiche correctement
> (raymarching d'une sphère avec glow) après les trois correctifs.

## Phase 5 — Limites d'export vs limites d'aperçu Shadertoy

- [x] **Résolutions non standards et ratios d'aspect arbitraires.** Le
  README documente des presets (4:3, 16:9, 9:16) plus une résolution
  personnalisée ; vérifier que `iResolution` et tout calcul dérivé
  (`fragCoord`/`uv`) restent corrects pour un ratio strictement arbitraire,
  comme peut le produire l'éditeur Shadertoy officiel.
  > Déjà correct, confirmé plutôt que supposé : rendu réel (via
  > `MultiPassRenderer` directement, hors UI) d'un shader dessinant un
  > cercle corrigé de l'aspect ratio à une résolution volontairement non
  > standard (777×333, ratio ~2.33:1) — le cercle mesure exactement le même
  > diamètre horizontalement et verticalement une fois normalisé par les
  > dimensions de chaque axe (ratio 1.0000 pile), confirmant que
  > `iResolution`/`fragCoord` (dérivés de `SV_Position` et des dimensions
  > réelles de la cible de rendu, `HlslBoilerplate.renameMainImage`) ne
  > contiennent aucune hypothèse cachée sur un ratio 16:9/4:3. Le sélecteur
  > de résolution **Custom** (largeur/hauteur libres) existe déjà dans le
  > panneau de réglages, donc aucun changement d'UI necessaire non plus.
- [x] **`iSampleRate` pour un fichier audio dont le taux d'échantillonnage
  diffère du taux de sortie configuré.** Vérifier que la valeur exposée au
  shader reflète bien le taux réel du fichier source (NAudio) et non une
  valeur fixe supposée.
  > Confirmé cassé puis corrigé : `MultiPassRenderer.UpdateUniforms` fixait
  > `SampleRate = 44100f` en dur, alors que `AudioTrack.SampleRate` (NAudio,
  > `AudioTrackLoader`) porte déjà le taux réel du fichier chargé — jamais
  > propagé jusqu'à l'uniforme. `BoundAudioAsset` porte désormais ce
  > `SampleRate` (peuplé dans `BoundAssetsBuilder`), et
  > `MultiPassRenderer.ResolveSampleRate` l'expose comme `iSampleRate` pour
  > le premier canal audio effectivement lié à la passe (Shadertoy n'expose
  > qu'un seul `iSampleRate` global, jamais par canal) — retombe sur 44100 Hz
  > uniquement si le shader n'utilise aucune entrée audio.
- [x] **Alignement des seeds/valeurs pseudo-aléatoires basées sur
  `iFrame`/`iTime` entre l'aperçu et l'export.** Un shader qui dérive un
  état pseudo-aléatoire de `iFrame` doit produire des résultats identiques
  en aperçu (lecture interactive, `PreviewClock`) et à l'export
  (`LoopCalculator`), déjà garanti en théorie par le principe de
  déterminisme du projet (voir `CLAUDE.md`) — ajouter un test de
  non-régression qui compare explicitement une frame donnée rendue par les
  deux chemins de code.
  > Écart réel trouvé : `iFrame` en aperçu était un simple compteur
  > incrémenté une fois par frame de rendu réellement affichée
  > (`MainWindowViewModel.RenderCurrentFrame`, `CurrentFrame++`), donc cadencé
  > sur le taux de rafraîchissement réel de la machine (~60 Hz, variable),
  > jamais sur le frame rate cible d'export — un shader dérivant un état
  > pseudo-aléatoire de `iFrame` affichait donc une valeur d'`iFrame`
  > sensiblement différente en aperçu qu'à l'export pour un même instant de
  > lecture. Corrigé : l'aperçu calcule maintenant `iFrame` comme
  > `floor(tempsDeLecture × frameRateExport)`, l'exacte relation inverse de
  > la construction de la timeline d'export
  > (`Core.LoopCalculator.buildFrameTimeline`, `TimeSeconds = index / frameRate`).
  > **Reste une approximation, pas une garantie stricte d'égalité
  > frame-à-frame** : l'aperçu n'est pas un rejeu de la timeline
  > déterministe de l'export, seulement une horloge de lecture temps réel
  > indépendante qui calcule maintenant le même `iFrame` qu'aurait l'export
  > pour cet instant précis — `iTime` continue lui de varier en continu
  > (temps réel écoulé) en aperçu contre des valeurs strictement quantifiées
  > par frame à l'export, ce qui reste une différence de nature entre les
  > deux chemins, inévitable pour une preview interactive. Test de
  > non-régression ajouté (`LoopCalculatorTests`) vérifiant que la formule
  > de l'aperçu retrouve bien l'index de frame exact d'une timeline d'export
  > construite pour plusieurs frame rates courants (24/30/60/23.976 fps).

> **Vérification** : `dotnet build`/`dotnet test` propres (35/35 tests, 4
> nouveaux pour cette phase). Item 1 vérifié par rendu D3D11 réel via
> `MultiPassRenderer` à une résolution non standard (777×333) avec mesure
> géométrique du résultat (cercle non déformé). Item 2 (`iSampleRate`) était
> un vrai bug (valeur fixe 44100 Hz) — corrigé et compilé, non re-testé avec
> un fichier audio à taux d'échantillonnage non standard faute d'un tel
> fichier disponible dans cet environnement (la correction elle-même,
> propager `AudioTrack.SampleRate` déjà résolu par NAudio, ne laisse pas de
> place à l'ambiguïté). Item 3 vérifié interactivement (aperçu affichant un
> `iFrame` cohérent avec le temps de lecture et le frame rate d'export
> sélectionné) en plus du test de non-régression Core.

## Phase 6 — Diagnostics et messages dédiés à la compatibilité Shadertoy

- [x] **Détection et message clair pour les uniformes/canaux non
  supportés à l'import.** Plutôt que de laisser un `iChannel` de type non
  géré échouer silencieusement ou planter la compilation, le panneau
  **Shader Issues** doit lister explicitement, par shader importé, tout
  élément détecté mais non encore couvert (ex. canal "Keyboard" avant la
  Phase 3, structure GLSL exotique avant la Phase 4), avec un lien vers la
  documentation correspondante plutôt qu'une simple erreur de compilation
  opaque.
  > "Keyboard"/"Mic" étaient déjà couverts par un avertissement explicite
  > depuis la Phase 3 (`unsupportedByDesignInputTypeMessage`). Gap réel
  > restant trouvé et corrigé : un type de canal `iChannel` qui n'est ni
  > l'un des types gérés (texture/buffer/video/cubemap/volume/music/
  > musicstream) ni l'un des types non supportés par design connus
  > (keyboard/mic) — un futur type Shadertoy jamais rencontré, ou une valeur
  > malformée dans un export tiers — faisait disparaître le canal
  > silencieusement, sans aucun avertissement. `detectUnsupportedChannel`
  > (`ShadertoyJsonParser.fs`) couvre désormais aussi ce cas générique avec
  > un message explicite nommant le type non reconnu. Une structure GLSL
  > exotique ou une erreur de transpilation restent, elles, déjà remontées
  > via les diagnostics `errorIssue`/`warningIssue` du transpileur
  > (`GlslToHlslTranspiler`/`HlslNativeTranspiler`/`WgslToHlslTranspiler`),
  > affichés dans le même panneau Shader Issues — aucun changement
  > nécessaire là. Tests de régression ajoutés (type inconnu → avertissement
  > nommant le type ; type reconnu → aucun faux positif).
- [x] **Page dédiée « Compatibilité Shadertoy » dans README.md.** Une fois
  les phases 2 à 4 largement couvertes, documenter précisément ce qui est
  supporté à 100%, ce qui est supporté avec une limitation assumée par
  design (déterminisme : `iMouse`/`iDate` figés, pas d'entrée micro live),
  et ce qui reste hors périmètre, pour fixer des attentes claires côté
  utilisateur qui importe directement des shaders copiés depuis
  shadertoy.com.
  > Nouvelle section « Shadertoy compatibility » ajoutée à `README.md`
  > (entre *Usage* et *Building from source*), avec les trois catégories
  > demandées : entièrement supporté (uniformes standards, types de canal,
  > attributs sampler filter/wrap/vflip, liaison de buffers par id, macros
  > de préprocesseur, ratios d'aspect arbitraires, etc.), supporté avec
  > limitation assumée par design (`iMouse`/`iDate` figés, canaux Keyboard/
  > Mic non supportés, sliders custom ignorés à l'export, décalage
  > preview/export sur `iTime`, `srgb`/`internal` non appliqués, conventions
  > cubemap/volume non vérifiées contre un export réel), et hors périmètre
  > (micro/clavier temps réel, intrinsèquement incompatibles avec un export
  > déterministe).

> **Vérification** : `dotnet build`/`dotnet test` propres (37/37 tests, 2
> nouveaux pour cette phase). Aperçu re-testé interactivement après le
> changement du parseur JSON (nouveau shader, rendu sans erreur) pour
> confirmer l'absence de régression sur le chemin de chargement standard.
>
> Avec cette phase, les 6 phases du ROADMAP sont closes. Points laissés
> comme limitations documentées plutôt que résolus (voir la section
> *Shadertoy compatibility* du README pour le détail complet) : `srgb`/
> `internal` par canal, conventions cubemap/volume non vérifiées contre un
> export shadertoy.com réel faute d'en avoir un disponible, et le TODO
> `tint.exe` resté ouvert dans les corrections initiales (item "Flags CLI de
> `tint.exe` non vérifiés" tout en haut de ce fichier).
