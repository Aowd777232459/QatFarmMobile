window.qatVoice = {
    listen: function () {
        return new Promise((resolve) => {
            const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
            if (!SpeechRecognition) {
                resolve({ success: false, text: '', message: 'التعرف الصوتي غير مدعوم في هذا المتصفح. استخدم Chrome أو Edge، أو اكتب الأمر.' });
                return;
            }
            try {
                const recognition = new SpeechRecognition();
                recognition.lang = 'ar-YE';
                recognition.interimResults = false;
                recognition.continuous = false;
                recognition.maxAlternatives = 3;
                let completed = false;
                const finish = (value) => {
                    if (completed) return;
                    completed = true;
                    resolve(value);
                };
                recognition.onresult = (event) => {
                    const text = event.results && event.results[0] && event.results[0][0]
                        ? event.results[0][0].transcript
                        : '';
                    finish({ success: !!text, text: text || '', message: text ? 'تم التقاط الأمر.' : 'لم يتم التقاط أمر واضح.' });
                };
                recognition.onerror = (event) => finish({ success: false, text: '', message: event.error === 'not-allowed' ? 'اسمح للمتصفح باستخدام الميكروفون ثم حاول مرة أخرى.' : 'تعذر التقاط الصوت: ' + event.error });
                recognition.onnomatch = () => finish({ success: false, text: '', message: 'لم يتم التعرف على الكلام بوضوح.' });
                recognition.onend = () => finish({ success: false, text: '', message: 'انتهى الاستماع بدون أمر واضح.' });
                recognition.start();
            } catch (error) {
                resolve({ success: false, text: '', message: 'تعذر تشغيل الميكروفون: ' + error.message });
            }
        });
    },
    speak: function (text) {
        try {
            if (!window.speechSynthesis || !text) return;
            window.speechSynthesis.cancel();
            const utterance = new SpeechSynthesisUtterance(text);
            utterance.lang = 'ar-SA';
            utterance.rate = 1;
            window.speechSynthesis.speak(utterance);
        } catch (_) { }
    },
    openUrl: function (url) {
        window.open(url, '_blank', 'noopener,noreferrer');
    }
};
