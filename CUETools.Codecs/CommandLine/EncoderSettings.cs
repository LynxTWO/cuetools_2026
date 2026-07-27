using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace CUETools.Codecs.CommandLine
{
    [JsonObject(MemberSerialization.OptIn)]
    public class EncoderSettings : IAudioEncoderSettings
    {
        #region IAudioEncoderSettings implementation
        [DefaultValue("")]
        [JsonProperty]
        public string Name { get; set; }

        [DefaultValue("")]
        [JsonProperty]
        public string Extension { get; set; }

        [Browsable(false)]
        public Type EncoderType => typeof(AudioEncoder);

        private bool _lossless;
        private bool _verificationRequired;
        private bool _deserializing;
        private int _verificationContractVersion;
        private bool _verificationUsesEncoder;
        private string _verificationPath;
        private string _verificationParameters;

        [JsonProperty]
        public bool Lossless
        {
            get { return _lossless; }
            set
            {
                _lossless = value;
                // Once a command encoder has been designated for a lossless face, toggling a
                // mutable UI label must never turn off its output verification in the live session.
                if (value && !_deserializing)
                {
                    _verificationRequired = true;
                    _verificationContractVersion = 1;
                }
            }
        }

        [Browsable(false)]
        [JsonProperty]
        public bool VerificationRequired
        {
            get { return _verificationRequired; }
            set
            {
                if (value)
                {
                    _verificationRequired = true;
                    if (!_deserializing)
                        _verificationContractVersion = 1;
                }
                else if (_deserializing)
                {
                    _verificationRequired = false;
                }
            }
        }

        /// <summary>
        /// Version 1 means a lossless command encoder is governed by the independent-decoder
        /// contract. Version 0 is reserved for profiles created before that contract existed:
        /// they remain usable, but explicitly unverified, until the user configures a decoder.
        /// </summary>
        [Browsable(false)]
        [DefaultValue(0)]
        [JsonProperty]
        public int VerificationContractVersion
        {
            get { return _verificationContractVersion; }
            set { _verificationContractVersion = value; }
        }

        [Browsable(false)]
        public bool UsesLegacyUnverifiedCompatibility
        {
            get { return Lossless && !VerificationRequired; }
        }

        [Browsable(false)]
        public int Priority => 0;

        [DefaultValue("")]
        [JsonProperty]
        public string SupportedModes { get; set; }

        public string DefaultMode => EncoderMode;

        [Browsable(false)]
        [DefaultValue("")]
        [JsonProperty]
        public string EncoderMode { get; set; }

        [Browsable(false)]
        public AudioPCMConfig PCM { get; set; }

        [Browsable(false)]
        public int BlockSize { get; set; }

        [Browsable(false)]
        [DefaultValue(4096)]
        public int Padding { get; set; }

        public IAudioEncoderSettings Clone()
        {
            return MemberwiseClone() as IAudioEncoderSettings;
        }
        #endregion

        public EncoderSettings()
        {
            this.Init();
            _verificationContractVersion = 1;
        }

        public EncoderSettings(
            string name,
            string extension,
            bool lossless,
            string supportedModes,
            string defaultMode,
            string path,
            string parameters
            )
        {
            this.Init();
            _verificationContractVersion = 1;
            Name = name;
            Extension = extension;
            Lossless = lossless;
            SupportedModes = supportedModes;
            Path = path;
            EncoderMode = defaultMode;
            Parameters = parameters;
        }

        [DefaultValue("")]
        [JsonProperty]
        public string Path
        {
            get;
            set;
        }

        /// <summary>
        /// Runtime-only identity supplied by a host after it validates an app-managed executable.
        /// These fields are deliberately not JSON properties: approval receipts remain host-owned,
        /// and a persisted settings blob must not be able to grant itself an approved identity.
        /// AudioEncoder rechecks this identity through a read-only handle immediately before launch
        /// and retains that deny-write/delete lease through any self-verification.
        /// </summary>
        [Browsable(false)]
        public string ApprovedExecutableSha256
        {
            get;
            set;
        }

        [Browsable(false)]
        public long ApprovedExecutableLength
        {
            get;
            set;
        }

        [DefaultValue("")]
        [JsonProperty]
        public string Parameters
        {
            get;
            set;
        }

        [DefaultValue(false)]
        [DisplayName("Verify with encoder")]
        [Description("Use the exact encoder executable to decode and independently verify lossless output.")]
        [JsonProperty]
        public bool VerificationUsesEncoder
        {
            get { return _verificationUsesEncoder; }
            set
            {
                _verificationUsesEncoder = value;
                EnableVerificationWhenConfigured();
            }
        }

        [DefaultValue("")]
        [DisplayName("Verification decoder")]
        [Description("Decoder executable used only for independent lossless verification. Leave empty when 'Verify with encoder' is enabled.")]
        [JsonProperty]
        public string VerificationPath
        {
            get { return _verificationPath; }
            set
            {
                _verificationPath = value;
                EnableVerificationWhenConfigured();
            }
        }

        [DefaultValue("")]
        [DisplayName("Verification arguments")]
        [Description("Arguments that decode %I to a WAV stream on standard output. Required for every command-line lossless encoder.")]
        [JsonProperty]
        public string VerificationParameters
        {
            get { return _verificationParameters; }
            set
            {
                _verificationParameters = value;
                EnableVerificationWhenConfigured();
            }
        }

        [Browsable(false)]
        public bool HasLosslessVerifier
        {
            get { return HasConfiguredLosslessVerifier; }
        }

        private bool HasConfiguredLosslessVerifier
        {
            get
            {
                if (String.IsNullOrEmpty(VerificationParameters) ||
                    !VerificationParameters.Contains("%I"))
                    return false;
                return VerificationUsesEncoder ^
                    !String.IsNullOrEmpty(VerificationPath);
            }
        }

        private void EnableVerificationWhenConfigured()
        {
            if (_deserializing || !_lossless || !HasConfiguredLosslessVerifier)
                return;
            _verificationRequired = true;
            _verificationContractVersion = 1;
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            // Constructors establish version 1 for newly-created settings. Reset before reading so
            // an absent marker is unambiguously a pre-contract profile.
            _deserializing = true;
            _verificationContractVersion = 0;
            _verificationRequired = false;
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            _deserializing = false;
            if (!_lossless)
                return;

            if (_verificationContractVersion >= 1 ||
                HasConfiguredLosslessVerifier)
            {
                _verificationRequired = true;
                _verificationContractVersion = 1;
            }
            else
            {
                // Compatibility is explicit and observable, not a fake verifier: old custom
                // encoders keep working, while UI and reports can label their output unverified.
                _verificationRequired = false;
            }
        }

        [DefaultValue(600000)]
        [DisplayName("Process timeout (ms)")]
        [Description("Maximum time an external encoder may make no input or exit progress. The encoder process is terminated when this limit is reached.")]
        [JsonProperty]
        public int ProcessTimeoutMilliseconds
        {
            get;
            set;
        }
    }
}
