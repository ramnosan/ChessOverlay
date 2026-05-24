namespace ChessOverlay

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Drawing
open System.Text.Json
open Microsoft.ML.OnnxRuntime
open Microsoft.ML.OnnxRuntime.Tensors

type YoloRawDetection =
    {
        ClassIndex: int
        Confidence: float
        Bounds: RectangleF
    }

type IYoloObjectDetector =
    abstract Detect: Bitmap -> YoloRawDetection list

type YoloOutputParserOptions =
    {
        InputSize: int
        ConfidenceThreshold: float
    }

module YoloLabels =
    let private normalize (value: string) =
        value.Trim().ToLowerInvariant().Replace("_", "").Replace("-", "").Replace(" ", "")

    let private tryKind value =
        match normalize value with
        | "pawn" | "p" | "whitepawn" | "blackpawn" -> Some Pawn
        | "knight" | "n" | "whiteknight" | "blackknight" -> Some Knight
        | "bishop" | "b" | "whitebishop" | "blackbishop" -> Some Bishop
        | "rook" | "r" | "whiterook" | "blackrook" -> Some Rook
        | "queen" | "q" | "whitequeen" | "blackqueen" -> Some Queen
        | "king" | "k" | "whiteking" | "blackking" -> Some King
        | _ -> None

    let private tryColor value =
        let normalized = normalize value
        let hasPieceAfterPrefix (prefix: string) =
            normalized.Length > prefix.Length
            && tryKind (normalized.Substring(prefix.Length)) |> Option.isSome

        if normalized.StartsWith("white") || normalized.EndsWith("white") || hasPieceAfterPrefix "w" then
            Some Bottom
        elif normalized.StartsWith("black") || normalized.EndsWith("black") || hasPieceAfterPrefix "b" then
            Some Top
        else
            None

    let tryPiece value =
        match tryColor value, tryKind value with
        | Some color, Some kind -> Some { Color = color; Kind = kind }
        | _ -> None

    let private stringValue (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.String -> element.GetString()
        | JsonValueKind.Object ->
            let mutable nameProperty = Unchecked.defaultof<JsonElement>

            if element.TryGetProperty("name", &nameProperty) then
                nameProperty.GetString()
            else
                null
        | _ -> null

    let private optionalLabel index value =
        if String.IsNullOrWhiteSpace value then
            None
        else
            Some(index, value)

    let private arrayLabels (root: JsonElement) =
        root.EnumerateArray()
        |> Seq.mapi (fun index value -> index, value.GetString())
        |> Seq.choose (fun (index, value) -> optionalLabel index value)
        |> Map.ofSeq

    let private objectLabel (property: JsonProperty) =
        match Int32.TryParse property.Name with
        | true, index -> stringValue property.Value |> optionalLabel index
        | _ -> None

    let private objectLabels (root: JsonElement) =
        root.EnumerateObject()
        |> Seq.choose objectLabel
        |> Map.ofSeq

    let load (path: string) =
        let text = IO.File.ReadAllText(path)
        use document = JsonDocument.Parse(text)

        match document.RootElement.ValueKind with
        | JsonValueKind.Array -> arrayLabels document.RootElement
        | JsonValueKind.Object -> objectLabels document.RootElement
        | _ -> Map.empty

module YoloPostProcessing =
    let private center (rect: RectangleF) =
        PointF(rect.Left + rect.Width / 2.0f, rect.Top + rect.Height / 2.0f)

    let private intersectionOverUnion (left: RectangleF) (right: RectangleF) =
        let x1 = max left.Left right.Left
        let y1 = max left.Top right.Top
        let x2 = min left.Right right.Right
        let y2 = min left.Bottom right.Bottom
        let width = max 0.0f (x2 - x1)
        let height = max 0.0f (y2 - y1)
        let intersection = width * height
        let union = left.Width * left.Height + right.Width * right.Height - intersection

        if union <= 0.0f then
            0.0
        else
            float (intersection / union)

    let nonMaxSuppress iouThreshold detections =
        let sorted = detections |> List.sortByDescending _.Confidence
        let kept = ResizeArray<YoloRawDetection>()

        for detection in sorted do
            let overlapsKept =
                kept
                |> Seq.exists (fun existing ->
                    existing.ClassIndex = detection.ClassIndex
                    && intersectionOverUnion existing.Bounds detection.Bounds > iouThreshold)

            if not overlapsKept then
                kept.Add detection

        kept |> Seq.toList

    let toBoardReading labels confidenceThreshold iouThreshold boardSize detections =
        let labelsByClass =
            labels
            |> Map.toSeq
            |> Seq.choose (fun (index, label) -> YoloLabels.tryPiece label |> Option.map (fun piece -> index, piece))
            |> Map.ofSeq

        let squareSize = float32 boardSize / 8.0f

        let mapped =
            detections
            |> List.filter (fun detection -> detection.Confidence >= confidenceThreshold)
            |> nonMaxSuppress iouThreshold
            |> List.choose (fun detection ->
                labelsByClass
                |> Map.tryFind detection.ClassIndex
                |> Option.bind (fun piece ->
                    let center = center detection.Bounds

                    if center.X < 0.0f || center.Y < 0.0f || center.X >= float32 boardSize || center.Y >= float32 boardSize then
                        None
                    else
                        let square =
                            {
                                File = int (center.X / squareSize)
                                Rank = int (center.Y / squareSize)
                            }

                        Some(square, piece, detection.Confidence)))

        let duplicateSquare =
            mapped
            |> List.countBy (fun (square, _, _) -> square)
            |> List.exists (fun (_, count) -> count > 1)

        if duplicateSquare then
            None
        else
            let board =
                mapped
                |> List.fold (fun state (square, piece, _) -> Map.add square piece state) BoardState.empty

            let confidence =
                match mapped with
                | [] -> 1.0
                | values -> values |> List.minBy (fun (_, _, confidence) -> confidence) |> fun (_, _, value) -> value

            Some { Board = board; Confidence = confidence }

module YoloOutputParser =
    let private tensorValue (values: float32 array) featureCount detectionCount anchor feature featureMajor =
        if featureMajor then
            values[feature * detectionCount + anchor]
        else
            values[anchor * featureCount + feature]

    let private dimensionsOf (tensor: Tensor<float32>) =
        let dimensions = tensor.Dimensions
        let result = Array.zeroCreate dimensions.Length

        for index in 0 .. dimensions.Length - 1 do
            result[index] <- dimensions[index]

        result

    let private outputShape dimensions =
        if dimensions |> Array.length <> 3 then
            None
        else
            let first = dimensions[1]
            let second = dimensions[2]
            let featureMajor = first < second
            let featureCount = if featureMajor then first else second
            let detectionCount = if featureMajor then second else first

            if featureCount < 6 then
                None
            else
                Some(featureMajor, featureCount, detectionCount)

    let private detectionAt options boardSize values featureMajor featureCount detectionCount anchor =
        let hasObjectness = featureCount > 16
        let classStart = if hasObjectness then 5 else 4
        let classCount = featureCount - classStart
        let scale = float32 boardSize / float32 options.InputSize

        let value = tensorValue values featureCount detectionCount anchor
        let cx = value 0 featureMajor
        let cy = value 1 featureMajor
        let width = value 2 featureMajor
        let height = value 3 featureMajor
        let objectness = if hasObjectness then value 4 featureMajor else 1.0f

        let bestClass, bestClassScore =
            [ 0 .. classCount - 1 ]
            |> List.fold
                (fun (bestClass, bestScore) classIndex ->
                    let score = value (classStart + classIndex) featureMajor

                    if score > bestScore then
                        classIndex, score
                    else
                        bestClass, bestScore)
                (-1, 0.0f)

        let confidence = objectness * bestClassScore

        if confidence < float32 options.ConfidenceThreshold then
            None
        else
            let normalized = max (max cx cy) (max width height) <= 2.0f
            let multiplier = if normalized then float32 options.InputSize else 1.0f
            let left = (cx * multiplier - width * multiplier / 2.0f) * scale
            let top = (cy * multiplier - height * multiplier / 2.0f) * scale
            let boxWidth = width * multiplier * scale
            let boxHeight = height * multiplier * scale

            Some
                {
                    ClassIndex = bestClass
                    Confidence = float confidence
                    Bounds = RectangleF(left, top, boxWidth, boxHeight)
                }

    let parse options boardSize (tensor: Tensor<float32>) =
        let values = tensor |> Seq.toArray

        match dimensionsOf tensor |> outputShape with
        | None -> []
        | Some(featureMajor, featureCount, detectionCount) ->
            [ for anchor in 0 .. detectionCount - 1 do
                  match detectionAt options boardSize values featureMajor featureCount detectionCount anchor with
                  | Some detection -> detection
                  | None -> () ]

[<ExcludeFromCodeCoverage>]
type OnnxYoloObjectDetector(modelPath: string, preferGpu: bool, ?inputSize: int, ?confidenceThreshold: float) =
    let inputSize = defaultArg inputSize 640
    let confidenceThreshold = defaultArg confidenceThreshold 0.25

    let createSessionOptions () =
        let options = new SessionOptions()

        if preferGpu then
            try
                options.AppendExecutionProvider_DML()
            with _ ->
                ()

        options

    let createSession () =
        let options = createSessionOptions ()

        try
            new InferenceSession(modelPath, options), options
        with
        | _ when preferGpu ->
            options.Dispose()
            let cpuOptions = new SessionOptions()
            new InferenceSession(modelPath, cpuOptions), cpuOptions

    let session, sessionOptions = createSession ()
    let inputName = session.InputMetadata.Keys |> Seq.head

    let tensorFromBitmap (bitmap: Bitmap) =
        use resized = new Bitmap(inputSize, inputSize)
        use graphics = Graphics.FromImage resized
        graphics.InterpolationMode <- Drawing2D.InterpolationMode.HighQualityBicubic
        graphics.DrawImage(bitmap, 0, 0, inputSize, inputSize)

        let tensor = DenseTensor<float32>([| 1; 3; inputSize; inputSize |])

        for y in 0 .. inputSize - 1 do
            for x in 0 .. inputSize - 1 do
                let color = resized.GetPixel(x, y)
                tensor[0, 0, y, x] <- float32 color.R / 255.0f
                tensor[0, 1, y, x] <- float32 color.G / 255.0f
                tensor[0, 2, y, x] <- float32 color.B / 255.0f

        tensor

    let parseOutput (boardSize: int) (tensor: Tensor<float32>) =
        YoloOutputParser.parse { InputSize = inputSize; ConfidenceThreshold = confidenceThreshold } boardSize tensor

    interface IYoloObjectDetector with
        member _.Detect(bitmap: Bitmap) =
            let input = tensorFromBitmap bitmap
            let inputs = new List<NamedOnnxValue>()
            inputs.Add(NamedOnnxValue.CreateFromTensor(inputName, input))
            use results = session.Run(inputs)
            let output = results |> Seq.head
            let tensor = output.AsTensor<float32>()
            parseOutput bitmap.Width tensor

    interface IDisposable with
        member _.Dispose() =
            session.Dispose()
            sessionOptions.Dispose()

type YoloBoardReader(detector: IYoloObjectDetector, labels: Map<int, string>, ?confidenceThreshold: float, ?iouThreshold: float) =
    let confidenceThreshold = defaultArg confidenceThreshold 0.45
    let iouThreshold = defaultArg iouThreshold 0.45

    interface IBoardReader with
        member _.Read(bitmap, geometry) =
            let cropBounds = Rectangle(geometry.Left, geometry.Top, geometry.Size, geometry.Size)

            if cropBounds.Left < 0
               || cropBounds.Top < 0
               || cropBounds.Right > bitmap.Width
               || cropBounds.Bottom > bitmap.Height then
                None
            else
                use crop = bitmap.Clone(cropBounds, bitmap.PixelFormat)
                detector.Detect crop
                |> YoloPostProcessing.toBoardReading labels confidenceThreshold iouThreshold geometry.Size
