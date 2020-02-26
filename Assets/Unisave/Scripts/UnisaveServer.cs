﻿using System;
using System.Collections.Generic;
using UnityEngine;
using Unisave.Facets;
using Unisave.Utils;
using RSG;
using LightJson;
using Unisave.Arango;
using Unisave.Contracts;

namespace Unisave
{
    /// <summary>
    /// Represents the unisave servers for the client.
    /// 
    /// Here end up all the facade calls.
    /// Here server emulation begins.
    /// </summary>
    public class UnisaveServer
    {
        /*
            This is the main class through which all user requests towards unisave go.

            Emulation
            =========

            Server emulation is, when all requests are diverted, they don't go to
            unisave servers, but to a local, emulated server.

            Emulation takes place in two scenarios:
            1) Developer starts a scene that already expects a logged-in player (eg. GarageScene)
                Unisave detects this by receiving a facet call request, while not being logged in.
            2) Developer explicitly states in UnisavePreferences that he wants to emulate.
                For example when lacking internet connection, but wanting to develop.

            Emulation replaces all aspects of the server:
            - player login / registration
            - facet calling
            - entity persistence

            When the scenario 1) occurs, we don't know what player to log into. Developer
            has to explicitly state the desired player email address in Unisave preferences
            under the field "Auto-login email". Player with this email has to exist inside the
            emulated database otherwise the login cannot be performed. Create this player
            by using standard registration with explicit emulation enabled.

            Emulation can never take place in a built game. It only happens, when running
            inside the unity editor.
         */
        
        /// <summary>
        /// The default instance used by all the facades
        /// </summary>
        public static UnisaveServer DefaultInstance
        {
            get
            {
                if (defaultInstance == null)
                    defaultInstance = CreateDefaultInstance();

                return defaultInstance;
            }
        }
        private static UnisaveServer defaultInstance = null;

        private static UnisaveServer CreateDefaultInstance()
        {
            // register promise exception handler
            Promise.UnhandledException += (object sender, ExceptionEventArgs e) => {
                Debug.LogError(e.Exception.ToString() + "\n\n");
            };

            // create new instance with proper preferences
            UnisaveServer instance = CreateFromPreferences(
                GetDefaultPreferencesWithOverriding()
            );

            // register framework services
            // TODO: this is broken, needs fixing
            //Foundation.Application.Default = new Foundation.Application();
            //Foundation.Application.Default.Instance<IDatabase>(instance.Database);

            return instance;
        }

        /// <summary>
        /// List of overriding preferences.
        /// Only the topmost preference is used (last in the list)
        /// </summary>
        private static List<UnisavePreferences> overridingPreferences = new List<UnisavePreferences>();

        /// <summary>
        /// Adds preferences to be used for overriding default preferences for the default instance
        /// </summary>
        public static void AddOverridingPreferences(UnisavePreferences preferences)
        {
            if (preferences == null)
                throw new ArgumentNullException();

            if (overridingPreferences.Contains(preferences))
                return;

            overridingPreferences.Add(preferences);

            defaultInstance?.ReloadPreferences(GetDefaultPreferencesWithOverriding());
        }

        /// <summary>
        /// Removes preferences used for overriding
        /// </summary>
        public static void RemoveOverridingPreferences(UnisavePreferences preferences)
        {
            if (preferences == null)
                throw new ArgumentNullException();

            overridingPreferences.Remove(preferences);

            defaultInstance?.ReloadPreferences(GetDefaultPreferencesWithOverriding());
        }

        /// <summary>
        /// Applies overriding preferences to the default instance
        /// </summary>
        private static UnisavePreferences GetDefaultPreferencesWithOverriding()
        {
            if (overridingPreferences.Count == 0)
                return UnisavePreferences.LoadOrCreate();

            return overridingPreferences[overridingPreferences.Count - 1];
        }

        /// <summary>
        /// Creates the instance from UnisavePreferences
        /// </summary>
        public static UnisaveServer CreateFromPreferences(UnisavePreferences preferences)
        {
            var server = new UnisaveServer(
                CoroutineRunnerComponent.GetInstance(),
                preferences.ServerUrl,
                preferences.GameToken,
                preferences.EditorKey,
                preferences.EmulatedDatabaseName,
                preferences.AutoLoginPlayerEmail,
                preferences.AutoRegisterPlayer,
                preferences.AutoRegisterArguments
            );

            if (preferences.AlwaysEmulate)
                server.IsEmulating = true;

            return server;
        }

        public UnisaveServer(
            CoroutineRunnerComponent coroutineRunner,
            string apiUrl,
            string gameToken,
            string editorKey,
            string emulatedDatabaseName,
            string autoLoginPlayerEmail,
            bool autoRegister,
            JsonObject autoRegisterArguments
        )
        {
            this.ApiUrl = new ApiUrl(apiUrl);
            this.GameToken = gameToken;
            this.EditorKey = editorKey;
            //this.emulatedDatabaseName = emulatedDatabaseName;
            //this.autoLoginPlayerEmail = autoLoginPlayerEmail;
            //this.autoRegister = autoRegister;
            //this.autoRegisterArguments = autoRegisterArguments;

            this.coroutineRunner = coroutineRunner;

            IsEmulating = false;
        }

        /// <summary>
        /// Call this method when preferences have been changed to apply the changes
        /// </summary>
        public void ReloadPreferences(UnisavePreferences preferences)
        {
            // emulated database name
            //emulatedDatabaseName = preferences.EmulatedDatabaseName;
            //emulatedDatabase = null; // make it reload once needed

            // always emulate
            if (preferences.AlwaysEmulate)
                IsEmulating = true;

            // email for auto login
            //autoLoginPlayerEmail = preferences.AutoLoginPlayerEmail;

            // auto registration
            //autoRegister = preferences.AutoRegisterPlayer;
            //autoRegisterArguments = preferences.AutoRegisterArguments;

            // TODO: apply remaining preferences
        }

        /// <summary>
        /// Url of the unisave server's API entrypoint ending with a slash
        /// </summary>
        public ApiUrl ApiUrl { get; private set; }

        /// <summary>
        /// Something, that can run coroutines
        /// </summary>
        private readonly CoroutineRunnerComponent coroutineRunner;

        /// <summary>
        /// Token that identifies this game to unisave servers
        /// </summary>
        public string GameToken { get; private set; }

        /// <summary>
        /// Token that authenticates this editor to unisave servers
        /// </summary>
        public string EditorKey { get; private set; }

        /// <summary>
        /// Is the server being emulated
        /// </summary>
        public bool IsEmulating
        {
            get => isEmulating;

            set
            {
                if (value == isEmulating)
                    return;

                if (value)
                {
                    // emulation cannot be started in runtime
                    // this may happen if the developer forgets the
                    // "Always emulate" option enabled during build
                    if (!Application.isEditor)
                        return;

                    Debug.LogWarning("Unisave: Starting server emulation.");
                    isEmulating = true;
                }
                else
                {
                    isEmulating = false;
                }
            }
        }
        private bool isEmulating = false;

        /// <summary>
        /// Facet caller that performs the calls against unisave servers
        /// </summary>
        public UnisaveFacetCaller UnisaveFacetCaller
        {
            get
            {
                if (unisaveFacetCaller == null)
                {
                    unisaveFacetCaller = new UnisaveFacetCaller(
                        ApiUrl,
                        GameToken
                    );
                }

                return unisaveFacetCaller;
            }
        }
        private UnisaveFacetCaller unisaveFacetCaller;

        /// <summary>
        /// Facet caller that emulates the calls locally against the emulated database
        /// </summary>
        public EmulatedFacetCaller EmulatedFacetCaller
        {
            get
            {
                if (emulatedFacetCaller == null)
                {
                    emulatedFacetCaller = new EmulatedFacetCaller();
                }

                return emulatedFacetCaller;
            }
        }
        private EmulatedFacetCaller emulatedFacetCaller;

        /// <summary>
        /// Handles facet calling once the player is authenticated
        /// If no player authenticated, emulated player gets logged in
        /// </summary>
        public FacetCaller FacetCaller
        {
            get
            {
                if (IsEmulating)
                {
//                    if (!EmulatedAuthenticator.LoggedIn)
//                    {
//                        EmulatedAuthenticator.AutoLogin(
//                            autoLoginPlayerEmail, autoRegister, autoRegisterArguments
//                        );
//                    }

                    return EmulatedFacetCaller;
                }
                else
                {
//                    if (!UnisaveAuthenticator.LoggedIn)
//                    {
//                        if (!Application.isEditor)
//                            throw new Exception("Cannot call facet methods without a logged-in player.");
//
//                        IsEmulating = true;
//
//                        EmulatedAuthenticator.AutoLogin(
//                            autoLoginPlayerEmail, autoRegister, autoRegisterArguments
//                        );
//
//                        return EmulatedFacetCaller;
//                    }

                    return UnisaveFacetCaller;
                }
            }
        }
    }
}