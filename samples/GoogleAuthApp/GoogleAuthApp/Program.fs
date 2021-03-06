module GoogleAuthApp.App

open System
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Authentication
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open FSharp.Control.Tasks.ContextInsensitive
open Giraffe
open Giraffe.GiraffeViewEngine
open GoogleAuthApp.HttpsConfig

// ---------------------------------
// Web app
// ---------------------------------

module AuthSchemes =

    let cookie = "Cookies"
    let google = "Google"

module Urls =

    let index      = "/"
    let login      = "/login"
    let googleAuth = "/google-auth"
    let user       = "/user"
    let logout     = "/logout"
    let missing    = "/missing"

module Views =

    let master (content: XmlNode list) =
        html [] [
            head [] [
                title [] [ rawText "Google Auth Sample App" ]
            ]
            body [] content
        ]

    let index =
        [
            h1 [] [ rawText "Google Auth Sample App" ]
            p [] [ rawText "Welcome to the Google Auth Sample App!" ]
            ul [] [
                li [] [ a [ _href Urls.login ] [ rawText "Login" ] ]
                li [] [ a [ _href Urls.user ] [ rawText "User profile" ] ]
            ]
        ] |> master

    let login =
        [
            h1 [] [ rawText "Login" ]
            p [] [ rawText "Pick one of the options to log in:" ]
            ul [] [
                li [] [ a [ _href Urls.googleAuth ] [ rawText "Google" ] ]
                li [] [ a [ _href Urls.missing ] [ rawText "Facebook" ] ]
                li [] [ a [ _href Urls.missing ] [ rawText "Twitter" ] ]
            ]
            p [] [
                a [ _href Urls.index ] [ rawText "Return to home." ]
            ]
        ] |> master

    let user (claims : (string * string) seq) =
        [
            h1 [] [ rawText "User details" ]
            h2 [] [ rawText "Claims:" ]
            ul [] [
                yield! claims |> Seq.map (
                    fun (key, value) ->
                        li [] [ sprintf "%s: %s" key value |> encodedText ] )
            ]
            p [] [
                a [ _href Urls.logout ] [ rawText "Logout" ]
            ]
        ] |> master

    let notFound =
        [
            h1 [] [ rawText "Not Found" ]
            p [] [ rawText "The requested resource does not exist." ]
            p [] [ rawText "Facebook and Twitter auth handlers have not been configured yet." ]
            ul [] [
                li [] [ a [ _href Urls.index ] [ rawText "Return to home." ] ]
            ]
        ] |> master

module Handlers =

    let index : HttpHandler = Views.index |> htmlView
    let login : HttpHandler = Views.login |> htmlView

    let user : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            (ctx.User.Claims
            |> Seq.map (fun c -> (c.Type, c.Value))
            |> Views.user
            |> htmlView) next ctx

    let logout : HttpHandler =
        signOut AuthSchemes.cookie
        >=> redirectTo false Urls.index

    let challenge (scheme : string) (redirectUri : string) : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                do! ctx.ChallengeAsync(
                        scheme,
                        AuthenticationProperties(RedirectUri = redirectUri))
                return! next ctx
            }

    let googleAuth = challenge AuthSchemes.google Urls.user

    let authenticate : HttpHandler =
        requiresAuthentication login

    let notFound : HttpHandler =
        setStatusCode 404 >=>
        (Views.notFound |> htmlView)

    let webApp : HttpHandler =
        choose [
            GET >=>
                choose [
                    route Urls.index      >=> index
                    route Urls.login      >=> login
                    route Urls.user       >=> authenticate >=> user
                    route Urls.logout     >=> logout
                    route Urls.googleAuth >=> googleAuth
                ]
            notFound ]

    let error (ex : Exception) (logger : ILogger) =
        logger.LogError(EventId(), ex, "An unhandled exception has occurred while executing the request.")
        clearResponse >=> setStatusCode 500 >=> text ex.Message

// ---------------------------------
// Config and Main
// ---------------------------------

let configureApp (app : IApplicationBuilder) =
    app.UseGiraffeErrorHandler(Handlers.error)
       .UseAuthentication()
       .UseGiraffe Handlers.webApp

let configureServices (services : IServiceCollection) =
    // Enable Authentication providers
    services.AddAuthentication(fun o -> o.DefaultScheme <- AuthSchemes.cookie)
            .AddCookie(
                AuthSchemes.cookie, fun o ->
                    o.LoginPath  <- PathString Urls.login
                    o.LogoutPath <- PathString Urls.logout)
            .AddGoogle(
                AuthSchemes.google, fun o ->
                    o.ClientId     <- "<google client id>"
                    o.ClientSecret <- "<google client secret>")
            |> ignore

    // Add Giraffe dependencies
    services.AddGiraffe() |> ignore

let configureLogging (builder : ILoggingBuilder) =
    let filter (l : LogLevel) = l.Equals LogLevel.Error
    builder.AddFilter(filter).AddConsole().AddDebug() |> ignore

[<EntryPoint>]
let main _ =
    let endpoints =
        [
            EndpointConfiguration.Default
            { EndpointConfiguration.Default with
                Port     = Some 44340
                Scheme   = Https
                FilePath = Some "<path to self signed certificate>"
                Password = Some "<password>" } ]

    WebHostBuilder()
        .UseKestrel(fun o -> o.ConfigureEndpoints endpoints)
        .Configure(Action<IApplicationBuilder> configureApp)
        .ConfigureServices(configureServices)
        .ConfigureLogging(configureLogging)
        .Build()
        .Run()
    0