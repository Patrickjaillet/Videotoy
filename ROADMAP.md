# Roadmap — Corrections à apporter

Liste des problèmes identifiés lors d'une revue de code du dépôt (rendu Direct3D 11,
pipeline d'export FFmpeg, transpileur de shaders, application WPF). Classés par
catégorie et par sévérité.

## Critique

- [ ] **FFmpeg : fuite de `Process` si `Process.Start()` échoue.** Dans
  `src/Videotoy.Ffmpeg/FfmpegService.cs` (`LaunchProcessAsync`, ~L237-266), si
  `Start()` lève une exception (ex. `ffmpeg.exe` manquant ou bloqué), `_process`
  reste non-null mais n'a jamais démarré. `IsRunning` (L40) appelle alors
  `HasExited` sur un process non démarré, ce qui lève `InvalidOperationException`
  et bloque définitivement le service (tous les exports suivants échouent jusqu'au
  redémarrage de l'appli). Il faut englober `Start()` dans un try/catch qui
  réinitialise `_process`/`_stdin` en cas d'échec.

- [ ] **Cache vidéo partagé ignorant la résolution → corruption mémoire GPU.**
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

## Élevé

- [ ] **Erreur "disque plein" reclassée à tort comme transitoire.**
  `src/Videotoy.Ffmpeg/TransientFfmpegErrorClassifier.cs` (L13-20) traite toute
  `IOException` comme transitoire, alors que le commentaire de la classe indique
  explicitement qu'un disque plein ne doit jamais être retenté. Un disque plein
  remonte comme `IOException` (pipe brisé) depuis `FfmpegService.WriteFrameAsync`
  avant que `FfmpegStderrDiagnosis` puisse le classifier correctement, ce qui fait
  relancer inutilement tout le pipeline de rendu (`VideoExportPipeline.RunAsync`,
  jusqu'à `MaxTransientRetries` fois) pour un export voué à l'échec.

- [ ] **Vérification d'intégrité FFmpeg (SHA-256) faite une seule fois, TOCTOU.**
  `src/Videotoy.Ffmpeg/FfmpegIntegrityVerifier.cs` (L14-40) n'est appelée qu'au
  démarrage (`App.xaml.cs` L42-58). Aucun des nombreux appels ultérieurs à
  `Process.Start()` (export, décodage vidéo, sonde matérielle) ne revérifie le
  binaire. Un remplacement du fichier après le démarrage (mise à jour ratée,
  logiciel malveillant, antivirus) passerait inaperçu.

- [ ] **Flags CLI de `tint.exe` non vérifiés (TODO explicite de l'auteur).**
  `src/Videotoy.Transpiler/WgslTranspilerProcess.cs` (L47-55) contient un TODO
  indiquant que les flags (`--format hlsl -o ...`) n'ont jamais été vérifiés
  contre le binaire réel qui sera distribué. Risque d'échec silencieux ou de
  comportement différent selon la version de `tint.exe` embarquée — à tester
  contre le binaire réellement livré dans `tools/tint/`.

- [ ] **`iMouse`, `iDate` et `iChannelResolution` toujours à zéro.**
  `src/Videotoy.Rendering/D3D11ShaderRenderer.cs` (L122-138) et
  `MultiPassRenderer.cs` (L631-647, code dupliqué) fixent ces uniformes
  Shadertoy standards à `Vector4.Zero`. Tout shader importé qui utilise
  l'interaction souris, la date, ou la résolution par canal (usages très
  courants sur Shadertoy) rendra un résultat visuellement incorrect, sans
  aucun diagnostic pour l'utilisateur.

- [ ] **Boucle d'export de la file de rendu exécutée sur le thread UI.**
  `src/Videotoy.App/ViewModels/MainWindowViewModel.cs` (`StartRenderQueueAsync`,
  ~L1577-1593) appelle `_renderQueueProcessor.StartAsync` sans `Task.Run`, et
  aucun `ConfigureAwait(false)` n'est utilisé dans toute la chaîne d'appels
  (`RenderQueueProcessor.cs` L245-310, `VideoExportPipeline.cs` L138-140). Le
  rendu D3D11 et la lecture des pixels (`Map` bloquant) s'exécutent donc entre
  les `await` sur le thread UI, provoquant des blocages/saccades pendant toute
  la durée d'un export par lot, surtout en haute résolution.

- [ ] **Aucun gestionnaire global d'exceptions non gérées.** Recherche sur tout
  le dépôt : ni `DispatcherUnhandledException`, ni
  `AppDomain.CurrentDomain.UnhandledException`, ni
  `TaskScheduler.UnobservedTaskException` (`src/Videotoy.App`). Combiné aux
  `async void` non protégés (voir plus bas), toute exception échappée plante
  l'application entière sans message ni log de crash.

- [ ] **Aucun projet de tests dans la solution.** `Videotoy.sln` ne référence
  que les 6 projets applicatifs, aucun projet de test. Zéro couverture
  automatisée pour la logique F# (`ShadertoyJsonParser`, `ShaderValidator`,
  `PassGraph`, `LoopCalculator`), la construction des arguments FFmpeg
  (`FfmpegService.BuildArguments`), `FfmpegStderrParser` /
  `TransientFfmpegErrorClassifier` (dont le bug 1.2 aurait été détecté par un
  simple test unitaire), ou les pipelines de rendu/export. Point structurant à
  traiter en priorité pour fiabiliser le reste.

## Moyen

- [ ] **`ShadertoyJsonParser` plante sur JSON malformé mais valide.**
  `src/Videotoy.Core/ShadertoyJsonParser.fs` : `tryGetStringValue` (L24-27) et
  `parseChannel` (L60-63) appellent `GetString()`/`GetInt32()` sans vérifier le
  type du `JsonElement`, ce qui lève `InvalidOperationException` non interceptée
  (seul `JsonException` est capturé, L144-146). Un fichier `.shadertoy`/`.json`
  partagé/téléchargé avec un champ mal typé plante le flux "Ouvrir un shader" au
  lieu d'afficher un message d'erreur clair.

- [ ] **Traversée de répertoire (path traversal) dans la résolution des assets
  de shader.** `src/Videotoy.Media/ShaderFileService.cs` (`ResolveAssetPath`,
  L246-249) ne normalise ni ne contraint le chemin résolu au répertoire de
  base : un `iChannel` avec `"src": "../../../../Windows/win.ini"` ou un chemin
  absolu est accepté sans validation, permettant de charger un fichier arbitraire
  du disque comme texture/audio/vidéo (mêmes points d'entrée dans `LoadTexture`,
  `LoadAudio`, `LoadVideo`).

- [ ] **Aucune gestion de la perte de périphérique GPU (DXGI device
  removed/TDR).** Aucune référence à `DeviceRemoved`, `GetDeviceRemovedReason`
  ou `DXGI_ERROR_DEVICE_REMOVED` dans `src/Videotoy.Rendering`. Un crash pilote,
  un timeout TDR, ou un changement de GPU pendant un export long produira une
  exception bas niveau opaque plutôt qu'un message clair "GPU perdu, veuillez
  réessayer".

- [ ] **`App.OnStartup` ne protège que la vérification FFmpeg.**
  `src/Videotoy.App/App.xaml.cs` (L31-70) : après le try/catch dédié à
  l'intégrité FFmpeg, `Services.GetRequiredService<Views.MainWindow>()`
  construit tout le graphe DI (dont la création du device D3D11) sans aucune
  protection. Un échec ici (pas de GPU/driver utilisable, DLL manquante) plante
  l'appli avec la boîte de dialogue générique Windows au lieu d'un message
  convivial comme pour le cas FFmpeg.

- [ ] **Nom de fichier de sortie non protégé contre la confusion avec un flag
  CLI.** `src/Videotoy.Ffmpeg/FfmpegService.cs` (L506, L228, L545, L555, L572) :
  le chemin de sortie est passé en dernier argument positionnel sans séparateur
  `--` ni validation. Un nom de fichier commençant par `-` serait interprété par
  FFmpeg comme une option.

## Faible

- [ ] **Échec de sonde matérielle avalé silencieusement.**
  `src/Videotoy.Ffmpeg/HardwareEncoderProbe.cs` (L180-185) : l'exception est
  entièrement ignorée (juste un commentaire), rendant les problèmes de détection
  d'encodage matériel impossibles à diagnostiquer depuis les rapports de bug
  utilisateurs. Ajouter au moins un log.

- [ ] **Fuite transitoire de shaders pixel COM en cas d'échec de compilation
  partiel.** `src/Videotoy.Rendering/MultiPassRenderer.cs` (`BuildPassGraph`,
  L288-340) : si la compilation d'une passe échoue après que des passes
  précédentes ont déjà créé leurs `ID3D11PixelShader`, ceux-ci ne sont libérés
  qu'au prochain chargement réussi ou à la fermeture de l'appli, pas
  immédiatement.

- [ ] **Gestionnaire `async void` non protégé pour le glisser-déposer vidéo.**
  `src/Videotoy.App/Views/MainWindow.xaml.cs` (`OnVideoChannelDrop`, L217-237) :
  toute exception dans `HandleFileDroppedAsync` est inobservable et, faute de
  gestionnaire global (voir plus haut), plante l'application.

- [ ] **Chaîne de localisation orpheline.**
  `src/Videotoy.App/Resources/Localization/en.json` et `fr.json` (L28) :
  `viewport.shaderLoaded.previewPending` n'est référencée nulle part dans le
  code alors que l'aperçu en direct est bien implémenté — probablement un
  reliquat d'un état "à venir" jamais nettoyé. À vérifier/supprimer, et auditer
  d'autres restes similaires de cette transition.

- [ ] **Duplication possible de logique de rendu mono-passe inutilisée.**
  `src/Videotoy.Rendering/D3D11ShaderRenderer.cs` et `NullShaderRenderer.cs` :
  aucune référence à `IShaderRenderer`/`D3D11ShaderRenderer`/`NullShaderRenderer`
  trouvée dans `src/Videotoy.App` (seul `MultiPassRenderer` semble câblé). Si
  confirmé mort, ce code duplique aussi le bug des uniformes figés à zéro
  (voir ci-dessus) et devrait être supprimé ou fusionné.

- [ ] **`MainWindowViewModel` ne se désabonne jamais de ses événements.**
  `src/Videotoy.App/ViewModels/MainWindowViewModel.cs` (L1002-1008) : quatre
  abonnements (`_previewClock.TimeChanged`, `_renderQueueProcessor.*`,
  `_historyStack.StateChanged`) sans `IDisposable` correspondant. Sans impact
  aujourd'hui (singleton vivant toute la durée de l'appli), mais deviendrait une
  fuite mémoire classique WPF si une seconde fenêtre/document était introduite.

- [ ] **Quelques chaînes françaises non traduites.**
  `src/Videotoy.App/Resources/Localization/fr.json` : certaines chaînes restent
  identiques à l'anglais alors qu'une traduction serait attendue, par ex.
  `statusBar.currentFrame` (`"Frame {0}"` → `"Image {0}"`) et
  `statusBar.frameCount.label` (`"Frame "`).
